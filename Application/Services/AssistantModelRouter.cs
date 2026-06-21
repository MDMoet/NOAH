using System.Text.RegularExpressions;
using Application.Configuration;
using Application.Interfaces;
using Application.Models;
using Microsoft.Extensions.Options;
using NOAH.Contracts.Assistant;

namespace Application.Services;

/// <summary>
/// Routes assistant requests between general-purpose and coding-focused models.
/// Uses weighted intent scoring instead of a single deterministic regex match.
/// </summary>
public sealed class AssistantModelRouter(
    IOptionsMonitor<LlmOptions> llmOptionsMonitor,
    IAssistantModelProcessManager assistantModelProcessManager,
    TimeProvider timeProvider)
    : IAssistantModelRouter
{
    private const int ExplicitIntentScore = 100;
    private const int CodingDecisionThreshold = 55;
    private const int CodingDecisionMargin = 10;

    private const string GeneralSystemPrompt =
        "You are NOAH's general assistant. Be practical, concise, and grounded in the supplied NOAH context. " +
        "Do not invent saved notes, tasks, reminders, locations, mileage entries, or prior conversation details.";

    private const string CodingSystemPrompt =
        "You are NOAH's coding assistant. Prioritize technical accuracy, debugging discipline, and concrete implementation guidance. " +
        "Use the supplied NOAH context as the source of truth and do not invent saved data.";

    private static readonly string[] ExplicitGeneralPhrases =
    [
        "use the general ai",
        "use general ai",
        "use the normal ai",
        "talk to the general ai",
        "switch to the general ai",
        "not a coding question",
        "non coding",
        "non-coding",
        "this is a general question",
        "general question",
        "normal question"
    ];

    private static readonly string[] ExplicitCodingPhrases =
    [
        "use the coding ai",
        "use coding ai",
        "talk to the coding ai",
        "switch to the coding ai",
        "coding question",
        "programming question",
        "developer question",
        "code question"
    ];

    private static readonly Regex SmallTalkRegex = new(
        @"^\s*(hi|hey|hello|yo|sup|good morning|good afternoon|good evening|thanks|thank you|ok|okay|nice|cool)\s*[!.?]*\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CodeBlockRegex = new(
        @"```|`[^`]+`",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StackTraceRegex = new(
        @"\b(exception|stack trace|traceback|NullReferenceException|InvalidOperationException|ArgumentException|HttpRequestException|SocketException|TaskCanceledException|DbUpdateException|ObjectDisposedException|IndexOutOfRangeException)\b|\.cs:line\s+\d+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FileNameOrExtensionRegex = new(
        @"\b[\w.\-]+\.(cs|csproj|sln|sql|json|xaml|xml|ts|tsx|js|jsx|css|scss|html|razor|cshtml|vue|php|py|java|kt|swift|yaml|yml|md)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CodingKeywordRegex = new(
        @"\b(maui|xaml|ef core|entity framework|linq|dependency injection|controller|service|repository|dto|endpoint|api|middleware|httpclient|mysql|stored procedure|regex|typescript|javascript|vue|react|angular|rider|visual studio|git|github|docker|class|interface|method|function|variable|namespace|constructor|async|await|task|compile|build|debug|refactor|bug|database|query|migration|localhost|kestrel|iis|powershell|processstartinfo|llama-server)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CodingActionRegex = new(
        @"\b(fix|debug|refactor|compile|build|implement|update my code|clean up|optimize|why does(n't| not) this work|why is this failing|make this work|add summary|write html|write css|write sql|create endpoint|add method|change this function)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex GeneralIntentRegex = new(
        @"\b(weather|time|date|remind|reminder|task|note|calendar|schedule|location|nearby|mileage|odometer|car|plant|keyboard|gym|food|recipe|invest|mortgage|translate|rewrite|improve this text|improve this message|email|message|what does this mean|explain this|summarize)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ProgrammingLanguageRegex = new(
        @"(?<![a-zA-Z0-9_])(c#|csharp|\.net|asp\.net|sql|mysql|typescript|javascript|html|css|xaml|json|xml|yaml|powershell)(?![a-zA-Z0-9_])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ApplicationTypeRegex = new(
        @"\b(console app|console application|web api|api|desktop app|maui app|windows app|android app|service|background service|worker service)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CreateCodeRequestRegex = new(
        @"\b(write|create|make|build|generate|implement|code|program)\b.*\b(app|application|program|script|class|method|function|endpoint|api|service|controller)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Creates a model-routing decision for the supplied request.
    /// </summary>
    /// <param name="request">The assistant request that needs model routing.</param>
    /// <returns>The selected model, fallback order, and role prompt.</returns>
    public AssistantModelRoutingDecision Route(AssistantCommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        LlmOptions llmOptions = llmOptionsMonitor.CurrentValue;
        string input = request.Input?.Trim() ?? string.Empty;

        DateTimeOffset evaluationTimeUtc = request.RequestedAtUtc == default
            ? timeProvider.GetUtcNow()
            : request.RequestedAtUtc.ToUniversalTime();

        string generalModelKey = ResolveGeneralModelKey(llmOptions);
        string codingModelKey = llmOptions.CodingModelKey;
        bool hasCodingModel = IsModelUsable(codingModelKey, llmOptions);

        AssistantModelSessionStatus codingSessionStatus = hasCodingModel
            ? assistantModelProcessManager.GetSessionStatus(codingModelKey, evaluationTimeUtc)
            : new AssistantModelSessionStatus(
                codingModelKey,
                false,
                null,
                "The coding model is unavailable.");

        RoutingScore score = CalculateRoutingScore(input, codingSessionStatus.IsActive);

        string selectedModelKey;
        string selectedSystemPrompt;
        string reason;
        bool usesCodingModel;

        if (!hasCodingModel)
        {
            selectedModelKey = generalModelKey;
            selectedSystemPrompt = GeneralSystemPrompt;
            reason = "The coding model is unavailable, so the general model was selected.";
            usesCodingModel = false;
        }
        else if (score.ExplicitlyGeneral)
        {
            selectedModelKey = generalModelKey;
            selectedSystemPrompt = GeneralSystemPrompt;
            reason = BuildReason(
                "The request explicitly prefers the general assistant.",
                score);
            usesCodingModel = false;
        }
        else if (score.ExplicitlyCoding)
        {
            selectedModelKey = codingModelKey;
            selectedSystemPrompt = CodingSystemPrompt;
            reason = BuildReason(
                "The request explicitly prefers the coding assistant.",
                score);
            usesCodingModel = true;
        }
        else if (ShouldUseCodingModel(score))
        {
            selectedModelKey = codingModelKey;
            selectedSystemPrompt = CodingSystemPrompt;
            reason = BuildReason(
                "The request looks coding-related, so the coding model was selected.",
                score);
            usesCodingModel = true;
        }
        else
        {
            selectedModelKey = generalModelKey;
            selectedSystemPrompt = GeneralSystemPrompt;
            reason = BuildReason(
                "The request looks general, so the general model was selected.",
                score);
            usesCodingModel = false;
        }

        if (usesCodingModel)
        {
            assistantModelProcessManager.RecordActivity(codingModelKey, timeProvider.GetUtcNow());
        }

        return new AssistantModelRoutingDecision(
            selectedModelKey,
            BuildFallbackModelKeys(
                selectedModelKey,
                generalModelKey,
                codingModelKey,
                usesCodingModel,
                llmOptions),
            selectedSystemPrompt,
            reason,
            usesCodingModel);
    }

    private static RoutingScore CalculateRoutingScore(string input, bool codingSessionIsActive)
    {
        int codingScore = 0;
        int generalScore = 0;
        List<string> reasons = [];

        bool explicitlyGeneral = ContainsAnyPhrase(input, ExplicitGeneralPhrases);
        bool explicitlyCoding = ContainsAnyPhrase(input, ExplicitCodingPhrases);

        if (string.IsNullOrWhiteSpace(input))
        {
            return new RoutingScore(
                0,
                100,
                false,
                false,
                ["The input is empty."]);
        }

        if (explicitlyGeneral)
        {
            generalScore += ExplicitIntentScore;
            reasons.Add("explicit general intent");
        }

        if (explicitlyCoding)
        {
            codingScore += ExplicitIntentScore;
            reasons.Add("explicit coding intent");
        }

        if (SmallTalkRegex.IsMatch(input))
        {
            generalScore += 60;
            reasons.Add("small-talk style input");
        }

        if (CodeBlockRegex.IsMatch(input))
        {
            codingScore += 80;
            reasons.Add("code formatting detected");
        }

        if (StackTraceRegex.IsMatch(input))
        {
            codingScore += 75;
            reasons.Add("exception or stack trace detected");
        }

        if (FileNameOrExtensionRegex.IsMatch(input))
        {
            codingScore += 50;
            reasons.Add("developer file name or extension detected");
        }

        if (CodingActionRegex.IsMatch(input))
        {
            codingScore += 45;
            reasons.Add("coding action detected");
        }

        if (CodingKeywordRegex.IsMatch(input))
        {
            codingScore += 35;
            reasons.Add("coding keyword detected");
        }

        if (GeneralIntentRegex.IsMatch(input))
        {
            generalScore += 35;
            reasons.Add("general assistant intent detected");
        }

        if (codingSessionIsActive && !explicitlyGeneral)
        {
            if (generalScore < 60)
            {
                codingScore += 30;
                reasons.Add("coding session is still active");
            }
            else
            {
                reasons.Add("coding session is active, but the request looks clearly general");
            }
        }

        if (input.Length <= 40 && codingScore == 0)
        {
            generalScore += 25;
            reasons.Add("short input without coding signals");
        }

        if (ProgrammingLanguageRegex.IsMatch(input))
        {
            codingScore += 70;
            reasons.Add("programming language detected");
        }

        if (ApplicationTypeRegex.IsMatch(input))
        {
            codingScore += 45;
            reasons.Add("application type detected");
        }

        if (CreateCodeRequestRegex.IsMatch(input))
        {
            codingScore += 55;
            reasons.Add("code creation request detected");
        }

        if (LooksLikeQuestion(input) && codingScore == 0)
        {
            generalScore += 15;
            reasons.Add("general question shape without coding signals");
        }

        return new RoutingScore(
            codingScore,
            generalScore,
            explicitlyGeneral,
            explicitlyCoding,
            reasons);
    }

    private static bool ShouldUseCodingModel(RoutingScore score)
    {
        if (score.CodingScore < CodingDecisionThreshold)
        {
            return false;
        }

        return score.CodingScore >= score.GeneralScore + CodingDecisionMargin;
    }

    private static string BuildReason(string mainReason, RoutingScore score)
    {
        string scoreText = $"Coding score: {score.CodingScore}. General score: {score.GeneralScore}.";

        if (score.Reasons.Count == 0)
        {
            return $"{mainReason} {scoreText}";
        }

        return $"{mainReason} {scoreText} Signals: {string.Join(", ", score.Reasons)}.";
    }

    private static bool LooksLikeQuestion(string input)
    {
        if (input.EndsWith('?'))
        {
            return true;
        }

        return input.StartsWith("what ", StringComparison.OrdinalIgnoreCase) ||
               input.StartsWith("why ", StringComparison.OrdinalIgnoreCase) ||
               input.StartsWith("how ", StringComparison.OrdinalIgnoreCase) ||
               input.StartsWith("when ", StringComparison.OrdinalIgnoreCase) ||
               input.StartsWith("where ", StringComparison.OrdinalIgnoreCase) ||
               input.StartsWith("can ", StringComparison.OrdinalIgnoreCase) ||
               input.StartsWith("should ", StringComparison.OrdinalIgnoreCase) ||
               input.StartsWith("do ", StringComparison.OrdinalIgnoreCase) ||
               input.StartsWith("does ", StringComparison.OrdinalIgnoreCase) ||
               input.StartsWith("is ", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveGeneralModelKey(LlmOptions llmOptions)
    {
        if (IsModelUsable(llmOptions.DefaultModelKey, llmOptions))
        {
            return llmOptions.DefaultModelKey;
        }

        foreach ((string modelKey, LlmModelOptions modelOptions) in llmOptions.Models)
        {
            if (modelOptions.Enabled && !string.IsNullOrWhiteSpace(modelOptions.BaseUrl))
            {
                return modelKey;
            }
        }

        throw new InvalidOperationException("No enabled LLM models are configured.");
    }

    private static IReadOnlyList<string> BuildFallbackModelKeys(
        string primaryModelKey,
        string generalModelKey,
        string codingModelKey,
        bool primaryUsesCodingModel,
        LlmOptions llmOptions)
    {
        List<string> fallbackModelKeys = [];

        if (primaryUsesCodingModel &&
            !string.Equals(primaryModelKey, generalModelKey, StringComparison.OrdinalIgnoreCase) &&
            IsModelUsable(generalModelKey, llmOptions))
        {
            fallbackModelKeys.Add(generalModelKey);
        }

        foreach ((string modelKey, LlmModelOptions modelOptions) in llmOptions.Models)
        {
            if (!modelOptions.Enabled ||
                string.IsNullOrWhiteSpace(modelOptions.BaseUrl) ||
                string.Equals(modelKey, primaryModelKey, StringComparison.OrdinalIgnoreCase) ||
                fallbackModelKeys.Contains(modelKey, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!primaryUsesCodingModel &&
                string.Equals(modelKey, codingModelKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            fallbackModelKeys.Add(modelKey);
        }

        return fallbackModelKeys;
    }

    private static bool IsModelUsable(string modelKey, LlmOptions llmOptions)
    {
        return !string.IsNullOrWhiteSpace(modelKey) &&
               llmOptions.Models.TryGetValue(modelKey, out LlmModelOptions? modelOptions) &&
               modelOptions.Enabled &&
               !string.IsNullOrWhiteSpace(modelOptions.BaseUrl);
    }

    private static bool ContainsAnyPhrase(string input, IReadOnlyList<string> phrases)
    {
        return phrases.Any(phrase => input.Contains(phrase, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record RoutingScore(
        int CodingScore,
        int GeneralScore,
        bool ExplicitlyGeneral,
        bool ExplicitlyCoding,
        IReadOnlyList<string> Reasons);
}
