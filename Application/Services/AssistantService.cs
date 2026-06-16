using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Application.Interfaces;
using Application.Models;
using Microsoft.Extensions.Logging;
using NOAH.Contracts.Assistant;
using NOAH.Contracts.Enums;
using NOAH.Domain.Entities;
using NOAH.Domain.Enums;

namespace Application.Services;

/// <summary>
/// Handles assistant-command processing, LLM execution, and interaction persistence.
/// </summary>
public sealed class AssistantService(
    ILlmClient llmClient,
    IAssistantInteractionRepository assistantInteractionRepository,
    IAssistantPromptBuilder assistantPromptBuilder,
    IAssistantToolService assistantToolService,
    IAssistantModelRouter assistantModelRouter,
    IAssistantModelProcessManager assistantModelProcessManager,
    TimeProvider timeProvider,
    ILogger<AssistantService> logger)
    : IAssistantService
{
    private const string GenericFailureResponseText =
        "Something went wrong while processing the assistant request.";

    private const string CancelledResponseText =
        "The assistant request was cancelled.";

    private const int StructuredPlannerMaxTokens = 2048;

    private static readonly JsonSerializerOptions PlannerSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly Regex FalseActionClaimRegex = new(
        @"\b(i('|’)ve| have)?\s*(created|scheduled|saved|added|set up|logged|updated)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StructuredActionIntentRegex = new(
        @"^(?:plan\s+this\b|schedule\b|set\s+(?:a\s+)?reminder\b|remind\s+me(?:\s+to)?\b|create\b|add\b|save\b|make\b|write\b|note(?:\s+down)?\b|search(?:\s+for)?\b|look\s+up\b|lookup\b|what\s+do\s+i\s+have\s+about\b|geocode\b|reverse\s+geocode\b|where\s+am\s+i\b|nearby\b|distance\b|calculate\b|log\s+mileage\b|mileage\s*:|backlog\b|overdue\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExplicitNoteIntentRegex = new(
        @"^(?:(?:create|add|save|make|write)\s+(?:(?:me|us)\s+)?(?:a\s+)?note\b|note(?:\s+down)?\b|notes?\s*:)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExplicitTaskIntentRegex = new(
        @"^(?:(?:create|add|make)\s+(?:(?:me|us)\s+)?(?:a\s+)?task\b|new\s+task\b|tasks?\s*:)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExplicitReminderIntentRegex = new(
        @"^(?:(?:create|add|set)\s+(?:(?:me|us)\s+)?(?:a\s+)?reminder\b|remind\s+me(?:\s+to)?\b|reminders?\s*:)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExplicitSearchIntentRegex = new(
        @"^(?:what\s+do\s+i\s+have\s+about\b|search(?:\s+(?:my|notes?|tasks?|reminders?|saved\s+locations?|locations?|mileage|assistant\s+history|conversation\s+history|for))?\b|find\s+my\b|show\s+(?:my|me\s+my)\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExplicitPlanningIntentRegex = new(
        @"^(?:show\s+me\s+(?:my\s+)?day\s+plan\b|show\s+day\s+plan\b|day\s+plan\b|plan\s+today\b|plan\s+tomorrow\b|planning\s+today\b|planning\s+tomorrow\b|today\s+plan\b|tomorrow\s+plan\b|week\s+plan\b|upcoming\s+plan\b|overdue\b|backlog\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // The planner schema stays intentionally small so the assistant can cheaply decide
    // whether a NOAH action should run before we fall back to open-ended text generation.
    private const string StructuredActionSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "actionType": {
              "type": "string",
              "enum": [ "Unknown", "CreateTask", "CreateNote", "CreateReminder", "Search", "ShowDayPlan" ]
            },
            "title": { "type": [ "string", "null" ] },
            "description": { "type": [ "string", "null" ] },
            "query": { "type": [ "string", "null" ] },
            "scheduledAt": { "type": [ "string", "null" ] },
            "endsAt": { "type": [ "string", "null" ] },
            "timeZoneId": { "type": [ "string", "null" ] },
            "priority": { "type": [ "string", "null" ] },
            "createLinkedReminder": { "type": "boolean" },
            "reminderAt": { "type": [ "string", "null" ] },
            "reminderTitle": { "type": [ "string", "null" ] },
            "reminderMessage": { "type": [ "string", "null" ] },
            "responseText": { "type": [ "string", "null" ] }
          },
          "required": [ "actionType", "createLinkedReminder" ]
        }
        """;

    /// <summary>
    /// Processes an assistant command, stores the interaction, and returns the final response.
    /// </summary>
    /// <param name="request">The assistant command request to process.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The assistant command response.</returns>
    public async Task<AssistantCommandResponse> ProcessMessageAsync(
        AssistantCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string input = request.Input?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("Input cannot be null or empty.", nameof(request.Input));
        }

        DateTimeOffset currentDateTimeUtc = timeProvider.GetUtcNow();
        AssistantResponseModeDto responseMode = request.PreferredResponseMode ?? AssistantResponseModeDto.Text;
        DateTimeOffset requestedAtUtc = request.RequestedAtUtc == default
            ? currentDateTimeUtc
            : request.RequestedAtUtc.ToUniversalTime();

        AssistantInteraction assistantInteraction = new()
        {
            Id = Guid.NewGuid(),
            UserInput = input,
            InputMode = (AssistantInputMode)request.InputMode,
            ActionType = AssistantActionType.Unknown,
            AssistantResponse = null,
            ResponseMode = (AssistantResponseMode)responseMode,
            Status = AssistantInteractionStatus.Received,
            RelatedEntityId = null,
            RelatedEntityType = null,
            ErrorMessage = null,
            RequestedAtUtc = requestedAtUtc,
            CompletedAtUtc = null,
            CreatedAtUtc = currentDateTimeUtc,
            UpdatedAtUtc = null
        };

        logger.LogInformation(
            "Processing assistant request {InteractionId} with input mode {InputMode}.",
            assistantInteraction.Id,
            request.InputMode);

        using IDisposable? logScope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["InteractionId"] = assistantInteraction.Id
        });
        Stopwatch requestStopwatch = Stopwatch.StartNew();

        // Store the received interaction before calling tools or the LLM so failed requests remain traceable.
        Stopwatch persistenceStopwatch = Stopwatch.StartNew();
        await assistantInteractionRepository.AddAsync(assistantInteraction, cancellationToken);
        logger.LogInformation(
            "Stored received assistant interaction in {ElapsedMs} ms.",
            GetElapsedMilliseconds(persistenceStopwatch));

        try
        {
            Stopwatch deterministicToolStopwatch = Stopwatch.StartNew();
            AssistantToolActionResult toolActionResult = await assistantToolService.TryExecuteAsync(
                new AssistantToolActionRequest(request with { Input = input, RequestedAtUtc = requestedAtUtc }, assistantInteraction.Id),
                cancellationToken);
            logger.LogInformation(
                "Deterministic tool evaluation completed in {ElapsedMs} ms. Handled: {WasHandled}. Action: {ActionType}.",
                GetElapsedMilliseconds(deterministicToolStopwatch),
                toolActionResult.WasHandled,
                toolActionResult.ActionType);

            if (toolActionResult.WasHandled)
            {
                assistantInteraction.ActionType = (AssistantActionType)toolActionResult.ActionType;
                assistantInteraction.AssistantResponse = NormalizeResponseText(toolActionResult.ResponseText);
                assistantInteraction.RelatedEntityId = toolActionResult.RelatedEntityId;
                assistantInteraction.RelatedEntityType = toolActionResult.RelatedEntityType;
                assistantInteraction.Status = AssistantInteractionStatus.Completed;
                assistantInteraction.CompletedAtUtc = timeProvider.GetUtcNow();
                assistantInteraction.UpdatedAtUtc = assistantInteraction.CompletedAtUtc;

                Stopwatch deterministicUpdateStopwatch = Stopwatch.StartNew();
                await assistantInteractionRepository.UpdateAsync(assistantInteraction, cancellationToken);

                logger.LogInformation(
                    "Assistant request was handled by deterministic tool action {ActionType}. Update persistence took {PersistenceElapsedMs} ms. Total request time: {TotalElapsedMs} ms.",
                    toolActionResult.ActionType,
                    GetElapsedMilliseconds(deterministicUpdateStopwatch),
                    GetElapsedMilliseconds(requestStopwatch));

                return MapToResponse(assistantInteraction);
            }

            Stopwatch conversationMemoryStopwatch = Stopwatch.StartNew();
            IReadOnlyList<AssistantConversationMemoryEntry> conversationMemory =
                await LoadConversationMemoryAsync(assistantInteraction.Id, cancellationToken);
            logger.LogInformation(
                "Loaded {ConversationMemoryCount} conversation memory item(s) in {ElapsedMs} ms.",
                conversationMemory.Count,
                GetElapsedMilliseconds(conversationMemoryStopwatch));

            Stopwatch contextStopwatch = Stopwatch.StartNew();
            AssistantPromptContext promptContext =
                (await assistantToolService.BuildContextAsync(
                    request with { Input = input, RequestedAtUtc = requestedAtUtc },
                    cancellationToken))
                with
                {
                    ConversationMemory = conversationMemory
                };
            logger.LogInformation(
                "Built assistant prompt context in {ElapsedMs} ms with {SearchResultCount} search result(s).",
                GetElapsedMilliseconds(contextStopwatch),
                promptContext.SearchResults.Count);

            Stopwatch routingStopwatch = Stopwatch.StartNew();
            AssistantModelRoutingDecision routingDecision = assistantModelRouter.Route(
                request with { Input = input, RequestedAtUtc = requestedAtUtc });
            logger.LogInformation(
                "Resolved assistant model routing in {ElapsedMs} ms. Primary model: {ModelKey}. Reason: {Reason}",
                GetElapsedMilliseconds(routingStopwatch),
                routingDecision.PrimaryModelKey,
                routingDecision.Reason);

            Stopwatch promptStopwatch = Stopwatch.StartNew();
            string prompt = assistantPromptBuilder.BuildPrompt(
                request with { Input = input, RequestedAtUtc = requestedAtUtc },
                promptContext);
            logger.LogInformation(
                "Built assistant free-form prompt in {ElapsedMs} ms. Prompt length: {PromptLength}.",
                GetElapsedMilliseconds(promptStopwatch),
                prompt.Length);

            assistantModelProcessManager.RecordActivity(
                routingDecision.PrimaryModelKey,
                requestedAtUtc);

            logger.LogInformation(
                "Assistant request {InteractionId} fell back to LLM processing with model {ModelKey}. Reason: {Reason}. Search results: {SearchResultCount}. Memory entries: {ConversationMemoryCount}.",
                assistantInteraction.Id,
                routingDecision.PrimaryModelKey,
                routingDecision.Reason,
                promptContext.SearchResults.Count,
                promptContext.ConversationMemory.Count);

            AssistantToolActionResult plannedActionResult = AssistantToolActionResult.NotHandled;

            if (ShouldAttemptStructuredActionPlanning(input))
            {
                Stopwatch structuredActionStopwatch = Stopwatch.StartNew();
                plannedActionResult = await TryExecuteStructuredActionAsync(
                    request with { Input = input, RequestedAtUtc = requestedAtUtc },
                    assistantInteraction.Id,
                    promptContext,
                    routingDecision,
                    cancellationToken);
                logger.LogInformation(
                    "Structured assistant action planning completed in {ElapsedMs} ms. Handled: {WasHandled}. Action: {ActionType}.",
                    GetElapsedMilliseconds(structuredActionStopwatch),
                    plannedActionResult.WasHandled,
                    plannedActionResult.ActionType);
            }
            else
            {
                logger.LogInformation(
                    "Skipped structured assistant action planning because the input did not match action-intent heuristics.");
            }

            if (plannedActionResult.WasHandled)
            {
                assistantInteraction.ActionType = (AssistantActionType)plannedActionResult.ActionType;
                assistantInteraction.AssistantResponse = NormalizeResponseText(plannedActionResult.ResponseText);
                assistantInteraction.RelatedEntityId = plannedActionResult.RelatedEntityId;
                assistantInteraction.RelatedEntityType = plannedActionResult.RelatedEntityType;
                assistantInteraction.Status = AssistantInteractionStatus.Completed;
                assistantInteraction.CompletedAtUtc = timeProvider.GetUtcNow();
                assistantInteraction.UpdatedAtUtc = assistantInteraction.CompletedAtUtc;

                Stopwatch structuredUpdateStopwatch = Stopwatch.StartNew();
                await assistantInteractionRepository.UpdateAsync(assistantInteraction, cancellationToken);

                logger.LogInformation(
                    "Assistant request was executed from structured LLM action {ActionType}. Update persistence took {PersistenceElapsedMs} ms. Total request time: {TotalElapsedMs} ms.",
                    plannedActionResult.ActionType,
                    GetElapsedMilliseconds(structuredUpdateStopwatch),
                    GetElapsedMilliseconds(requestStopwatch));

                return MapToResponse(assistantInteraction);
            }

            Stopwatch llmStopwatch = Stopwatch.StartNew();
            LlmChatCompletionResult llmResult = await llmClient.GenerateResponseAsync(
                new LlmChatCompletionRequest(
                    routingDecision.PrimaryModelKey,
                    prompt,
                    routingDecision.SystemPrompt,
                    routingDecision.FallbackModelKeys),
                cancellationToken);

            assistantModelProcessManager.RecordActivity(
                llmResult.ModelKey,
                timeProvider.GetUtcNow());

            assistantInteraction.AssistantResponse = NormalizeFallbackResponseText(llmResult.ResponseText);
            assistantInteraction.Status = AssistantInteractionStatus.Completed;
            assistantInteraction.CompletedAtUtc = timeProvider.GetUtcNow();
            assistantInteraction.UpdatedAtUtc = assistantInteraction.CompletedAtUtc;

            Stopwatch updateStopwatch = Stopwatch.StartNew();
            await assistantInteractionRepository.UpdateAsync(assistantInteraction, cancellationToken);

            logger.LogInformation(
                "Assistant request completed through the free-form LLM path with model {ModelKey} (provider model {ProviderModel}, fallback used: {UsedFallback}, total tokens: {TotalTokens}). LLM call took {LlmElapsedMs} ms. Update persistence took {PersistenceElapsedMs} ms. Total request time: {TotalElapsedMs} ms.",
                llmResult.ModelKey,
                llmResult.ProviderModel,
                llmResult.UsedFallback,
                llmResult.Usage?.TotalTokens,
                GetElapsedMilliseconds(llmStopwatch),
                GetElapsedMilliseconds(updateStopwatch),
                GetElapsedMilliseconds(requestStopwatch));

            return MapToResponse(assistantInteraction);
        }
        catch (OperationCanceledException exception)
        {
            DateTimeOffset cancelledAtUtc = timeProvider.GetUtcNow();

            assistantInteraction.Status = AssistantInteractionStatus.Cancelled;
            assistantInteraction.ErrorMessage = "The assistant request was cancelled.";
            assistantInteraction.AssistantResponse = CancelledResponseText;
            assistantInteraction.CompletedAtUtc = cancelledAtUtc;
            assistantInteraction.UpdatedAtUtc = cancelledAtUtc;

            await TryPersistTerminalStateAsync(assistantInteraction);

            logger.LogWarning(
                exception,
                "Assistant request was cancelled while processing input: {Input}. Total elapsed: {TotalElapsedMs} ms.",
                input,
                GetElapsedMilliseconds(requestStopwatch));

            return MapToResponse(assistantInteraction);
        }
        catch (Exception exception)
        {
            DateTimeOffset failedAtUtc = timeProvider.GetUtcNow();

            assistantInteraction.Status = AssistantInteractionStatus.Failed;
            assistantInteraction.ErrorMessage = exception.Message;
            assistantInteraction.AssistantResponse = GenericFailureResponseText;
            assistantInteraction.CompletedAtUtc = failedAtUtc;
            assistantInteraction.UpdatedAtUtc = failedAtUtc;

            await TryPersistTerminalStateAsync(assistantInteraction);

            logger.LogError(
                exception,
                "Error processing assistant request: {Input}. Total elapsed: {TotalElapsedMs} ms.",
                input,
                GetElapsedMilliseconds(requestStopwatch));

            return MapToResponse(assistantInteraction);
        }
    }

    /// <summary>
    /// Maps a persisted assistant interaction to an assistant command response.
    /// </summary>
    /// <param name="assistantInteraction">The interaction to map.</param>
    /// <returns>The mapped assistant command response.</returns>
    private static AssistantCommandResponse MapToResponse(AssistantInteraction assistantInteraction)
    {
        return new AssistantCommandResponse(
            assistantInteraction.Id,
            (AssistantActionTypeDto)assistantInteraction.ActionType,
            (AssistantInteractionStatusDto)assistantInteraction.Status,
            NormalizeResponseText(assistantInteraction.AssistantResponse),
            (AssistantResponseModeDto)assistantInteraction.ResponseMode,
            assistantInteraction.RelatedEntityId,
            assistantInteraction.RelatedEntityType);
    }

    private static string NormalizeResponseText(string? responseText)
    {
        return string.IsNullOrWhiteSpace(responseText)
            ? string.Empty
            : responseText.Trim();
    }

    /// <summary>
    /// Runs the structured planner and executes the resulting NOAH action when one is produced.
    /// </summary>
    private async Task<AssistantToolActionResult> TryExecuteStructuredActionAsync(
        AssistantCommandRequest request,
        Guid interactionId,
        AssistantPromptContext promptContext,
        AssistantModelRoutingDecision routingDecision,
        CancellationToken cancellationToken)
    {
        try
        {
            AssistantActionTypeDto? explicitActionHint = GetExplicitStructuredActionHint(request.Input);
            AssistantPlannedToolAction? plannedAction = await TryPlanStructuredActionCoreAsync(
                request,
                promptContext,
                routingDecision,
                cancellationToken);

            if (plannedAction == null || plannedAction.ActionType == AssistantActionTypeDto.Unknown)
            {
                logger.LogInformation(
                    "Structured assistant action planner did not produce an executable action.");
                return AssistantToolActionResult.NotHandled;
            }

            logger.LogInformation(
                "Structured assistant action planner produced action {ActionType}.",
                plannedAction.ActionType);

            if (explicitActionHint.HasValue &&
                plannedAction.ActionType != explicitActionHint.Value)
            {
                logger.LogWarning(
                    "Discarded structured assistant action {PlannedActionType} because the explicit user intent was {ExplicitActionType}.",
                    plannedAction.ActionType,
                    explicitActionHint.Value);
                return AssistantToolActionResult.NotHandled;
            }

            return await assistantToolService.ExecutePlannedActionAsync(
                new AssistantPlannedToolActionRequest(
                    request,
                    interactionId,
                    plannedAction),
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Structured assistant action planning failed for interaction {InteractionId}. Falling back to text generation.",
                interactionId);

            return AssistantToolActionResult.NotHandled;
        }
    }

    /// <summary>
    /// Asks the configured LLM for a compact structured action plan using schema-first parsing.
    /// </summary>
    private async Task<AssistantPlannedToolAction?> TryPlanStructuredActionCoreAsync(
        AssistantCommandRequest request,
        AssistantPromptContext promptContext,
        AssistantModelRoutingDecision routingDecision,
        CancellationToken cancellationToken)
    {
        string plannerPrompt = BuildStructuredActionPlannerPrompt(request, promptContext);
        LlmChatCompletionRequest[] plannerRequests =
        [
            new(
                routingDecision.PrimaryModelKey,
                plannerPrompt,
                "You are NOAH's tool planner. Return only one JSON object matching the schema. Do not include reasoning, markdown, or explanations. /no_think",
                routingDecision.FallbackModelKeys,
                StructuredOutput: new LlmStructuredOutputRequest(
                    "assistant_action_plan",
                    StructuredActionSchema),
                MaxTokensOverride: StructuredPlannerMaxTokens),
            new(
                routingDecision.PrimaryModelKey,
                plannerPrompt + Environment.NewLine + Environment.NewLine +
                "Return only one raw JSON object. Do not use markdown fences. Do not explain the answer.",
                "You are NOAH's tool planner. Return strict JSON only. /no_think",
                routingDecision.FallbackModelKeys,
                MaxTokensOverride: StructuredPlannerMaxTokens)
        ];

        foreach (LlmChatCompletionRequest plannerRequest in plannerRequests)
        {
            int attemptNumber = Array.IndexOf(plannerRequests, plannerRequest) + 1;
            Stopwatch plannerAttemptStopwatch = Stopwatch.StartNew();
            LlmChatCompletionResult plannerResult = await llmClient.GenerateResponseAsync(
                plannerRequest,
                cancellationToken);
            AssistantPlannedToolAction? plannedAction = TryDeserializePlannedAction(plannerResult.ResponseText);

            logger.LogInformation(
                "Structured planner attempt {AttemptNumber} completed in {ElapsedMs} ms. Used schema: {UsesStructuredOutput}. Parsed action: {ActionType}.",
                attemptNumber,
                GetElapsedMilliseconds(plannerAttemptStopwatch),
                plannerRequest.StructuredOutput != null,
                plannedAction?.ActionType.ToString() ?? "None");

            if (plannedAction != null)
            {
                return plannedAction;
            }
        }

        return null;
    }

    /// <summary>
    /// Builds the planner prompt used to decide whether the request should create or query NOAH data.
    /// </summary>
    private static string BuildStructuredActionPlannerPrompt(
        AssistantCommandRequest request,
        AssistantPromptContext promptContext)
    {
        StringBuilder promptBuilder = new();
        AssistantActionTypeDto? explicitActionHint = GetExplicitStructuredActionHint(request.Input);

        promptBuilder.AppendLine("Decide whether this user request should trigger one NOAH tool action.");
        promptBuilder.AppendLine("Prefer a concrete action when the user wants to create, plan, schedule, remind, search, or show planning data.");
        promptBuilder.AppendLine("Only use Search when the user is asking about data already stored in NOAH. Do not use Search for general knowledge or open-ended discussion.");
        promptBuilder.AppendLine("If the user is simply asking a question, discussing ideas, or requesting normal conversation, return actionType Unknown.");
        promptBuilder.AppendLine("For CreateNote, generate the stored note content itself. Use title for a concise note title and description for the full note body.");
        promptBuilder.AppendLine("When the user asks you to note, write, draft, or save content for them, do not just repeat the command words. Actually write the requested content.");
        promptBuilder.AppendLine("For CreateTask and CreateReminder, generate clean stored titles and descriptions/messages instead of copying the request verbatim when a better result is obvious.");
        promptBuilder.AppendLine("If the user wants to plan or schedule an event with a real date/time, use CreateTask and set createLinkedReminder to true.");
        promptBuilder.AppendLine("For scheduled events, set reminderAt to the same start time unless the user explicitly asks for a different reminder time.");
        promptBuilder.AppendLine("Use scheduledAt for the event start time and endsAt for the event end time when known.");
        promptBuilder.AppendLine("For overnight events whose end time is earlier than the start time, set endsAt to the next calendar date.");
        promptBuilder.AppendLine("Use Europe/Amsterdam as the default local time zone when the user gives local times without a zone.");
        promptBuilder.AppendLine("Use ISO-8601 date-times with offsets when a time is known, for example 2026-06-20T23:00:00+02:00.");
        promptBuilder.AppendLine("If no tool should run, return actionType Unknown.");
        promptBuilder.AppendLine("Return only the JSON object. Do not write analysis, prose, markdown, or a <think> section.");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine($"Detected explicit action hint: {explicitActionHint?.ToString() ?? "None"}");
        promptBuilder.AppendLine($"Current UTC time: {promptContext.CurrentDateTimeUtc:u}");
        promptBuilder.AppendLine($"Requested at UTC: {request.RequestedAtUtc.ToUniversalTime():u}");
        promptBuilder.AppendLine("Relevant NOAH search results:");

        if (promptContext.SearchResults.Count == 0)
        {
            promptBuilder.AppendLine("- None found.");
        }
        else
        {
            foreach (AssistantContextSearchResult searchResult in promptContext.SearchResults.Take(5))
            {
                promptBuilder.Append("- ");
                promptBuilder.Append(searchResult.Type);
                promptBuilder.Append(": ");
                promptBuilder.Append(searchResult.Title);

                if (!string.IsNullOrWhiteSpace(searchResult.Preview))
                {
                    promptBuilder.Append(" - ");
                    promptBuilder.Append(searchResult.Preview);
                }

                promptBuilder.AppendLine();
            }
        }

        promptBuilder.AppendLine();
        promptBuilder.AppendLine("Recent conversation memory:");

        if (promptContext.ConversationMemory.Count == 0)
        {
            promptBuilder.AppendLine("- None available.");
        }
        else
        {
            foreach (AssistantConversationMemoryEntry memoryEntry in promptContext.ConversationMemory.Take(6))
            {
                promptBuilder.Append("- ");
                promptBuilder.Append(memoryEntry.RequestedAtUtc.ToUniversalTime().ToString("u"));
                promptBuilder.Append(" | ");
                promptBuilder.Append(memoryEntry.ActionType);
                promptBuilder.Append(" | User: ");
                promptBuilder.AppendLine(memoryEntry.UserInput);
            }
        }

        promptBuilder.AppendLine();
        promptBuilder.AppendLine("User request:");
        promptBuilder.AppendLine(request.Input);
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("/no_think");

        return promptBuilder.ToString();
    }

    private static AssistantPlannedToolAction? TryDeserializePlannedAction(string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return null;
        }

        string normalizedResponseText = responseText.Trim();

        if (TryDeserializePlannedActionJson(normalizedResponseText, out AssistantPlannedToolAction? plannedAction))
        {
            return plannedAction;
        }

        string withoutMarkdownFence = StripMarkdownCodeFence(normalizedResponseText);

        if (!string.Equals(withoutMarkdownFence, normalizedResponseText, StringComparison.Ordinal) &&
            TryDeserializePlannedActionJson(withoutMarkdownFence, out plannedAction))
        {
            return plannedAction;
        }

        int firstBraceIndex = normalizedResponseText.IndexOf('{');
        int lastBraceIndex = normalizedResponseText.LastIndexOf('}');

        if (firstBraceIndex >= 0 && lastBraceIndex > firstBraceIndex)
        {
            string jsonSlice = normalizedResponseText[firstBraceIndex..(lastBraceIndex + 1)];

            if (TryDeserializePlannedActionJson(jsonSlice, out plannedAction))
            {
                return plannedAction;
            }
        }

        return null;
    }

    private static bool TryDeserializePlannedActionJson(
        string json,
        out AssistantPlannedToolAction? plannedAction)
    {
        try
        {
            plannedAction = JsonSerializer.Deserialize<AssistantPlannedToolAction>(
                json,
                PlannerSerializerOptions);
            return plannedAction != null;
        }
        catch (JsonException)
        {
            plannedAction = null;
            return false;
        }
    }

    private static string StripMarkdownCodeFence(string value)
    {
        if (!value.StartsWith("```", StringComparison.Ordinal))
        {
            return value;
        }

        string trimmedValue = value.Trim();
        int firstNewLineIndex = trimmedValue.IndexOf('\n');

        if (firstNewLineIndex < 0)
        {
            return value;
        }

        string withoutOpeningFence = trimmedValue[(firstNewLineIndex + 1)..];
        int closingFenceIndex = withoutOpeningFence.LastIndexOf("```", StringComparison.Ordinal);

        return closingFenceIndex >= 0
            ? withoutOpeningFence[..closingFenceIndex].Trim()
            : withoutOpeningFence.Trim();
    }

    private static string NormalizeFallbackResponseText(string responseText)
    {
        string normalizedResponseText = NormalizeResponseText(responseText);

        if (!FalseActionClaimRegex.IsMatch(normalizedResponseText))
        {
            return normalizedResponseText;
        }

        return "I could not confirm that NOAH executed that action. Please try again, or use a more direct command.";
    }

    /// <summary>
    /// Filters requests that are worth sending through structured action planning.
    /// </summary>
    private static bool ShouldAttemptStructuredActionPlanning(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        string normalizedInput = NormalizeIntentInput(input);

        return GetExplicitStructuredActionHint(normalizedInput).HasValue ||
               StructuredActionIntentRegex.IsMatch(normalizedInput);
    }

    /// <summary>
    /// Detects strong explicit action intent before the planner gets a chance to generalize it.
    /// </summary>
    private static AssistantActionTypeDto? GetExplicitStructuredActionHint(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        string normalizedInput = NormalizeIntentInput(input);

        if (ExplicitNoteIntentRegex.IsMatch(normalizedInput))
        {
            return AssistantActionTypeDto.CreateNote;
        }

        if (ExplicitTaskIntentRegex.IsMatch(normalizedInput))
        {
            return AssistantActionTypeDto.CreateTask;
        }

        if (ExplicitReminderIntentRegex.IsMatch(normalizedInput))
        {
            return AssistantActionTypeDto.CreateReminder;
        }

        if (ExplicitSearchIntentRegex.IsMatch(normalizedInput))
        {
            return AssistantActionTypeDto.Search;
        }

        if (ExplicitPlanningIntentRegex.IsMatch(normalizedInput))
        {
            return AssistantActionTypeDto.ShowDayPlan;
        }

        return null;
    }

    /// <summary>
    /// Removes polite prefixes so intent matching focuses on the operative part of the request.
    /// </summary>
    private static string NormalizeIntentInput(string input)
    {
        string normalizedInput = input.Trim();
        normalizedInput = Regex.Replace(
            normalizedInput,
            @"^\s*(?:(?:hey|hi)\s+noah[\s,:\-]*)?(?:(?:please|can you|could you|would you|will you|can u|could u|would u)\s+)+",
            string.Empty,
            RegexOptions.IgnoreCase);

        return normalizedInput.TrimStart(':', '-', '.', ',', ' ').Trim();
    }

    private static double GetElapsedMilliseconds(Stopwatch stopwatch)
    {
        return Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2);
    }

    private async Task<IReadOnlyList<AssistantConversationMemoryEntry>> LoadConversationMemoryAsync(
        Guid currentInteractionId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AssistantInteraction> recentCompletedInteractions =
            await assistantInteractionRepository.GetRecentCompletedAsync(
                6,
                currentInteractionId,
                cancellationToken);

        return recentCompletedInteractions
            .OrderBy(assistantInteraction => assistantInteraction.RequestedAtUtc)
            .Select(assistantInteraction => new AssistantConversationMemoryEntry(
                assistantInteraction.Id,
                assistantInteraction.UserInput,
                NormalizeResponseText(assistantInteraction.AssistantResponse),
                (AssistantActionTypeDto)assistantInteraction.ActionType,
                assistantInteraction.RequestedAtUtc))
            .ToList();
    }

    private async Task TryPersistTerminalStateAsync(AssistantInteraction assistantInteraction)
    {
        try
        {
            await assistantInteractionRepository.UpdateAsync(
                assistantInteraction,
                CancellationToken.None);
        }
        catch (Exception persistenceException)
        {
            logger.LogWarning(
                persistenceException,
                "Failed to persist terminal assistant interaction state for {InteractionId}.",
                assistantInteraction.Id);
        }
    }
}
