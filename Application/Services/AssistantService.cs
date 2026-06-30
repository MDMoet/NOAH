using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Application.Interfaces;
using Application.Models;
using Microsoft.Extensions.Logging;
using NOAH.Contracts.Assistant;
using NOAH.Contracts.Common;
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
    IAssistantSettingsService assistantSettingsService,
    IAssistantChatService assistantChatService,
    IAssistantMemoryService assistantMemoryService,
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
        @"\b(?:(?:i'?ve|i\s+have|we'?ve|we\s+have)\s+)?(?:created|scheduled|saved|added|set up|logged|updated)\b|\bcreated\s+(?:note|task|reminder|memory|location|mileage)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const string PseudoToolNameRegexPattern =
        @"create[\s_]*(?:note|task|reminder|memory|mileage[\s_]*entry)|find[\s_]*nearby[\s_]*places|save[\s_]*(?:(?:my|this|current)[\s_]*)?location|calculate[\s_]*distance|geocode|reverse[\s_]*geocode|log[\s_]*mileage|search";

    private static readonly Regex PseudoToolCallRegex = new(
        $@"\b(?:{PseudoToolNameRegexPattern})\s*\([^)\r\n]*\)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex RecoverablePseudoToolCallRegex = new(
        $@"(?<tool>{PseudoToolNameRegexPattern})\s*\((?<args>[^)\r\n]*)\)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex StructuredActionIntentRegex = new(
        @"^(?:plan\s+this\b|schedule\b|set\s+(?:a\s+)?reminder\b|remind\s+me(?:\s+to)?\b|remember\b|keep\s+in\s+mind\b|for\s+future\s+reference\b|create\b|add\b|save\b|make\b|write\b|note(?:\s+down)?\b|search(?:\s+for)?\b|look\s+up\b|lookup\b|what\s+do\s+i\s+have\s+about\b|geocode\b|reverse\s+geocode\b|where\s+am\s+i\b|where\s+is\b|find\s+coordinates\s+for\b|save\s+(?:my|this|current)\s+location\b|location\s*:|nearby\b|near\s+me\b|around\s+me\b|closest\b|nearest\b|distance(?:\s+(?:to|from))?\b|how\s+far\b|calculate\b|log\s+mileage\b|mileage\s*:|backlog\b|overdue\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SimpleAcknowledgementRegex = new(
        @"^\s*(?:thanks?|thank\s+you|ty|thx|ok(?:ay)?|got\s+it|sounds\s+good|nice|cool|great|good|perfect|wonderful|awesome|excellent|lovely|brilliant|sweet)\s*[!.?]*\s*$",
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

    private static readonly Regex ExplicitMemoryIntentRegex = new(
        @"^(?:remember\b|keep\s+in\s+mind\b|for\s+future\s+reference\b|save\s+this\s+for\s+later\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExplicitSaveLocationIntentRegex = new(
        @"^(?:save\s+(?:my|this|current)\s+location\b|save\s+location\b|add\s+location\b|location\s*:)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExplicitNearbyIntentRegex = new(
        @"\b(?:nearby|near\s+me|around\s+me|closest|nearest|places?\s+nearby)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExplicitDistanceIntentRegex = new(
        @"^(?:calculate\s+distance\b|distance(?:\s+(?:to|from))?\b|how\s+far\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExplicitMileageIntentRegex = new(
        @"^(?:create\s+mileage\s+entry\b|add\s+mileage\s+entry\b|log\s+mileage\b|mileage\s*:)",
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
              "enum": [ "Unknown", "CreateTask", "CreateNote", "CreateReminder", "CreateMemory", "CreateMileageEntry", "Search", "ShowDayPlan", "SaveLocation", "FindNearbyPlaces", "CalculateDistance" ]
            },
            "title": { "type": [ "string", "null" ] },
            "description": { "type": [ "string", "null" ] },
            "tags": { "type": [ "string", "null" ] },
            "query": { "type": [ "string", "null" ] },
            "scheduledAt": { "type": [ "string", "null" ] },
            "endsAt": { "type": [ "string", "null" ] },
            "timeZoneId": { "type": [ "string", "null" ] },
            "priority": { "type": [ "string", "null" ] },
            "isPinned": { "type": "boolean" },
            "createLinkedReminder": { "type": "boolean" },
            "reminderAt": { "type": [ "string", "null" ] },
            "reminderTitle": { "type": [ "string", "null" ] },
            "reminderMessage": { "type": [ "string", "null" ] },
            "responseText": { "type": [ "string", "null" ] }
          },
          "required": [ "actionType", "isPinned", "createLinkedReminder" ]
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
        Stopwatch settingsStopwatch = Stopwatch.StartNew();
        AssistantSettingsDto assistantSettings = await assistantSettingsService.GetSettingsAsync(cancellationToken);
        logger.LogInformation(
            "Loaded assistant settings in {ElapsedMs} ms.",
            GetElapsedMilliseconds(settingsStopwatch));

        AssistantResponseModeDto responseMode = request.PreferredResponseMode ?? assistantSettings.PreferredResponseMode;
        DateTimeOffset requestedAtUtc = request.RequestedAtUtc == default
            ? currentDateTimeUtc
            : request.RequestedAtUtc.ToUniversalTime();

        AssistantInteraction assistantInteraction = new()
        {
            Id = Guid.NewGuid(),
            ChatId = request.ChatId,
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

        if (assistantInteraction.ChatId.HasValue)
        {
            Stopwatch chatActivityStopwatch = Stopwatch.StartNew();
            await assistantChatService.RecordInteractionAsync(
                assistantInteraction.ChatId.Value,
                input,
                requestedAtUtc,
                cancellationToken);
            logger.LogInformation(
                "Recorded assistant chat activity in {ElapsedMs} ms.",
                GetElapsedMilliseconds(chatActivityStopwatch));
        }

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

            string? acknowledgementResponse = CreateSimpleAcknowledgementResponse(input);

            if (acknowledgementResponse != null)
            {
                assistantInteraction.AssistantResponse = acknowledgementResponse;
                assistantInteraction.Status = AssistantInteractionStatus.Completed;
                assistantInteraction.CompletedAtUtc = timeProvider.GetUtcNow();
                assistantInteraction.UpdatedAtUtc = assistantInteraction.CompletedAtUtc;

                Stopwatch acknowledgementUpdateStopwatch = Stopwatch.StartNew();
                await assistantInteractionRepository.UpdateAsync(assistantInteraction, cancellationToken);

                logger.LogInformation(
                    "Assistant request was answered as a simple acknowledgement. Update persistence took {PersistenceElapsedMs} ms. Total request time: {TotalElapsedMs} ms.",
                    GetElapsedMilliseconds(acknowledgementUpdateStopwatch),
                    GetElapsedMilliseconds(requestStopwatch));

                return MapToResponse(assistantInteraction);
            }

            Stopwatch conversationMemoryStopwatch = Stopwatch.StartNew();
            IReadOnlyList<AssistantConversationMemoryEntry> conversationMemory =
                assistantSettings.EnableChatMemory && assistantSettings.ConversationMemoryMessageCount > 0
                    ? await LoadConversationMemoryAsync(
                        assistantInteraction.Id,
                        assistantInteraction.ChatId,
                        assistantSettings.ConversationMemoryMessageCount,
                        cancellationToken)
                    : Array.Empty<AssistantConversationMemoryEntry>();
            logger.LogInformation(
                "Loaded {ConversationMemoryCount} conversation memory item(s) in {ElapsedMs} ms.",
                conversationMemory.Count,
                GetElapsedMilliseconds(conversationMemoryStopwatch));

            Stopwatch chatPromptStopwatch = Stopwatch.StartNew();
            AssistantChatPromptInfo? chatPromptInfo = assistantInteraction.ChatId.HasValue
                ? await assistantChatService.GetPromptInfoAsync(assistantInteraction.ChatId.Value, cancellationToken)
                : null;
            logger.LogInformation(
                "Loaded assistant chat prompt info in {ElapsedMs} ms. Has chat: {HasChat}.",
                GetElapsedMilliseconds(chatPromptStopwatch),
                chatPromptInfo != null);

            Stopwatch longTermMemoryStopwatch = Stopwatch.StartNew();
            IReadOnlyList<AssistantLongTermMemoryEntry> longTermMemory =
                assistantSettings.EnableLongTermMemory && assistantSettings.LongTermMemoryItemCount > 0
                    ? await assistantMemoryService.GetRelevantMemoryAsync(
                        input,
                        assistantSettings.LongTermMemoryItemCount,
                        cancellationToken)
                    : Array.Empty<AssistantLongTermMemoryEntry>();
            logger.LogInformation(
                "Loaded {LongTermMemoryCount} long-term memory item(s) in {ElapsedMs} ms.",
                longTermMemory.Count,
                GetElapsedMilliseconds(longTermMemoryStopwatch));

            Stopwatch contextStopwatch = Stopwatch.StartNew();
            AssistantPromptContext promptContext =
                (await assistantToolService.BuildContextAsync(
                    request with { Input = input, RequestedAtUtc = requestedAtUtc },
                    cancellationToken))
                with
                {
                    Chat = chatPromptInfo,
                    LongTermMemory = longTermMemory,
                    ConversationMemory = conversationMemory
                };
            logger.LogInformation(
                "Built assistant prompt context in {ElapsedMs} ms with {SearchResultCount} search result(s), {ConversationMemoryCount} conversation entries, and {LongTermMemoryCount} long-term memories.",
                GetElapsedMilliseconds(contextStopwatch),
                promptContext.SearchResults.Count,
                promptContext.ConversationMemory.Count,
                promptContext.LongTermMemory.Count);

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

            AssistantToolActionResult recoveredPseudoToolActionResult =
                await TryRecoverPseudoToolActionAsync(
                    request with { Input = input, RequestedAtUtc = requestedAtUtc },
                    assistantInteraction.Id,
                    llmResult.ResponseText,
                    cancellationToken);

            if (recoveredPseudoToolActionResult.WasHandled)
            {
                assistantInteraction.ActionType = (AssistantActionType)recoveredPseudoToolActionResult.ActionType;
                assistantInteraction.AssistantResponse = NormalizeResponseText(recoveredPseudoToolActionResult.ResponseText);
                assistantInteraction.RelatedEntityId = recoveredPseudoToolActionResult.RelatedEntityId;
                assistantInteraction.RelatedEntityType = recoveredPseudoToolActionResult.RelatedEntityType;
                assistantInteraction.Status = AssistantInteractionStatus.Completed;
                assistantInteraction.CompletedAtUtc = timeProvider.GetUtcNow();
                assistantInteraction.UpdatedAtUtc = assistantInteraction.CompletedAtUtc;

                Stopwatch recoveredActionUpdateStopwatch = Stopwatch.StartNew();
                await assistantInteractionRepository.UpdateAsync(assistantInteraction, cancellationToken);

                logger.LogWarning(
                    "Recovered executable action {ActionType} from pseudo tool syntax emitted by the free-form LLM. Update persistence took {PersistenceElapsedMs} ms. Total request time: {TotalElapsedMs} ms.",
                    recoveredPseudoToolActionResult.ActionType,
                    GetElapsedMilliseconds(recoveredActionUpdateStopwatch),
                    GetElapsedMilliseconds(requestStopwatch));

                return MapToResponse(assistantInteraction);
            }

            assistantInteraction.AssistantResponse = NormalizeFallbackResponseText(
                llmResult.ResponseText,
                input);
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
            assistantInteraction.ChatId,
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

    private static string? CreateSimpleAcknowledgementResponse(string input)
    {
        if (!SimpleAcknowledgementRegex.IsMatch(input))
        {
            return null;
        }

        return Regex.IsMatch(input, @"\b(?:thanks?|thank\s+you|ty|thx)\b", RegexOptions.IgnoreCase)
            ? "You're welcome."
            : "Glad to hear it.";
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
            }
            else
            {
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
                }
                else
                {
                    AssistantToolActionResult executionResult =
                        await assistantToolService.ExecutePlannedActionAsync(
                            new AssistantPlannedToolActionRequest(
                                request,
                                interactionId,
                                plannedAction),
                            cancellationToken);

                    if (executionResult.WasHandled)
                    {
                        return executionResult;
                    }

                    logger.LogWarning(
                        "Structured assistant action {ActionType} could not be executed. Attempting an explicit-action fallback if one is available.",
                        plannedAction.ActionType);
                }
            }

            if (!explicitActionHint.HasValue)
            {
                return AssistantToolActionResult.NotHandled;
            }

            AssistantPlannedToolAction? fallbackAction = await TryPlanExplicitActionAsync(
                request,
                promptContext,
                routingDecision,
                explicitActionHint.Value,
                cancellationToken);

            if (fallbackAction == null || fallbackAction.ActionType != explicitActionHint.Value)
            {
                logger.LogInformation(
                    "Explicit structured fallback for action {ActionType} did not produce an executable plan.",
                    explicitActionHint.Value);
                return AssistantToolActionResult.NotHandled;
            }

            AssistantToolActionResult fallbackExecutionResult =
                await assistantToolService.ExecutePlannedActionAsync(
                    new AssistantPlannedToolActionRequest(
                        request,
                        interactionId,
                        fallbackAction),
                    cancellationToken);

            if (!fallbackExecutionResult.WasHandled)
            {
                logger.LogWarning(
                    "Explicit structured fallback for action {ActionType} still could not be executed.",
                    explicitActionHint.Value);
            }

            return fallbackExecutionResult;
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
        return await TryPlanStructuredActionWithPromptAsync(
            plannerPrompt,
            routingDecision,
            "general",
            cancellationToken);
    }

    private async Task<AssistantPlannedToolAction?> TryPlanExplicitActionAsync(
        AssistantCommandRequest request,
        AssistantPromptContext promptContext,
        AssistantModelRoutingDecision routingDecision,
        AssistantActionTypeDto explicitActionType,
        CancellationToken cancellationToken)
    {
        string plannerPrompt = BuildExplicitActionPlannerPrompt(
            request,
            promptContext,
            explicitActionType);
        return await TryPlanStructuredActionWithPromptAsync(
            plannerPrompt,
            routingDecision,
            $"explicit:{explicitActionType}",
            cancellationToken);
    }

    private async Task<AssistantPlannedToolAction?> TryPlanStructuredActionWithPromptAsync(
        string plannerPrompt,
        AssistantModelRoutingDecision routingDecision,
        string scenarioName,
        CancellationToken cancellationToken)
    {
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

        for (int attemptIndex = 0; attemptIndex < plannerRequests.Length; attemptIndex++)
        {
            LlmChatCompletionRequest plannerRequest = plannerRequests[attemptIndex];
            int attemptNumber = attemptIndex + 1;
            Stopwatch plannerAttemptStopwatch = Stopwatch.StartNew();
            LlmChatCompletionResult plannerResult = await llmClient.GenerateResponseAsync(
                plannerRequest,
                cancellationToken);
            AssistantPlannedToolAction? plannedAction = TryDeserializePlannedAction(plannerResult.ResponseText);

            logger.LogInformation(
                "Structured planner ({ScenarioName}) attempt {AttemptNumber} completed in {ElapsedMs} ms. Used schema: {UsesStructuredOutput}. Parsed action: {ActionType}.",
                scenarioName,
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
        promptBuilder.AppendLine("For CreateMemory, store durable facts, preferences, or reference details the user explicitly wants remembered. Use title for a concise label and description for the actual memory text.");
        promptBuilder.AppendLine("For FindNearbyPlaces, use query for the place type or search term, such as cafe, gas station, or pharmacy.");
        promptBuilder.AppendLine("For SaveLocation, use title for the saved location name and rely on currentLocation or coordinates already present in the request.");
        promptBuilder.AppendLine("For CalculateDistance, keep the user's coordinates or destination wording in query or description so NOAH can execute it.");
        promptBuilder.AppendLine("For CreateMileageEntry, only use that action when the user is clearly trying to log an odometer reading or mileage value.");
        promptBuilder.AppendLine("Never put pseudo tool syntax like find_nearby_places(...) or create_note(...) in responseText.");
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
        promptBuilder.AppendLine("Active chat:");

        if (promptContext.Chat == null)
        {
            promptBuilder.AppendLine("- Ad-hoc conversation.");
        }
        else
        {
            promptBuilder.Append("- ");
            promptBuilder.Append(promptContext.Chat.Title);

            if (!string.IsNullOrWhiteSpace(promptContext.Chat.Description))
            {
                promptBuilder.Append(" - ");
                promptBuilder.Append(promptContext.Chat.Description);
            }

            promptBuilder.AppendLine();
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
        promptBuilder.AppendLine("Relevant long-term memory:");

        if (promptContext.LongTermMemory.Count == 0)
        {
            promptBuilder.AppendLine("- None available.");
        }
        else
        {
            foreach (AssistantLongTermMemoryEntry memoryEntry in promptContext.LongTermMemory.Take(6))
            {
                promptBuilder.Append("- ");
                promptBuilder.Append(memoryEntry.Title);

                if (memoryEntry.IsPinned)
                {
                    promptBuilder.Append(" [pinned]");
                }

                promptBuilder.Append(": ");
                promptBuilder.Append(memoryEntry.Content);

                if (!string.IsNullOrWhiteSpace(memoryEntry.Tags))
                {
                    promptBuilder.Append(" (tags: ");
                    promptBuilder.Append(memoryEntry.Tags);
                    promptBuilder.Append(')');
                }

                promptBuilder.AppendLine();
            }
        }

        promptBuilder.AppendLine();
        promptBuilder.AppendLine("User request:");
        promptBuilder.AppendLine(request.Input);
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("/no_think");

        return promptBuilder.ToString();
    }

    private static string BuildExplicitActionPlannerPrompt(
        AssistantCommandRequest request,
        AssistantPromptContext promptContext,
        AssistantActionTypeDto explicitActionType)
    {
        StringBuilder promptBuilder = new();
        promptBuilder.AppendLine($"The user clearly wants the NOAH action {explicitActionType}.");
        promptBuilder.AppendLine($"Return one valid JSON object for actionType {explicitActionType}.");
        promptBuilder.AppendLine("Do not return Unknown, and do not choose a different action.");
        promptBuilder.AppendLine("Never include pseudo tool syntax in responseText.");
        promptBuilder.AppendLine("Only include concrete values that help NOAH execute the action.");

        switch (explicitActionType)
        {
            case AssistantActionTypeDto.CreateNote:
                promptBuilder.AppendLine("CreateNote guidance:");
                promptBuilder.AppendLine("- Write an actual note title in title.");
                promptBuilder.AppendLine("- Write the full stored note content in description.");
                promptBuilder.AppendLine("- If the user asks you to write a story, list, draft, summary, or message, put the actual content in description.");
                break;
            case AssistantActionTypeDto.CreateTask:
                promptBuilder.AppendLine("CreateTask guidance:");
                promptBuilder.AppendLine("- Put a concise actionable title in title.");
                promptBuilder.AppendLine("- Put useful task details in description.");
                promptBuilder.AppendLine("- Use scheduledAt when the user gives a due date or start time.");
                break;
            case AssistantActionTypeDto.CreateReminder:
                promptBuilder.AppendLine("CreateReminder guidance:");
                promptBuilder.AppendLine("- Put the reminder title in reminderTitle or title.");
                promptBuilder.AppendLine("- Put the reminder message in reminderMessage or description.");
                promptBuilder.AppendLine("- Set reminderAt when the user gives a time.");
                break;
            case AssistantActionTypeDto.CreateMemory:
                promptBuilder.AppendLine("CreateMemory guidance:");
                promptBuilder.AppendLine("- Use title for a short memory label.");
                promptBuilder.AppendLine("- Use description for the durable fact or preference to store.");
                break;
            case AssistantActionTypeDto.CreateMileageEntry:
                promptBuilder.AppendLine("CreateMileageEntry guidance:");
                promptBuilder.AppendLine("- Put the odometer or mileage text in description.");
                promptBuilder.AppendLine("- Preserve the reading value clearly so NOAH can parse it.");
                break;
            case AssistantActionTypeDto.Search:
                promptBuilder.AppendLine("Search guidance:");
                promptBuilder.AppendLine("- Put only the NOAH search phrase in query.");
                promptBuilder.AppendLine("- Do not use Search for open-ended questions or general knowledge.");
                break;
            case AssistantActionTypeDto.ShowDayPlan:
                promptBuilder.AppendLine("ShowDayPlan guidance:");
                promptBuilder.AppendLine("- Use scheduledAt when the user specifies a day.");
                promptBuilder.AppendLine("- Keep responseText empty unless there is a clear natural confirmation.");
                break;
            case AssistantActionTypeDto.SaveLocation:
                promptBuilder.AppendLine("SaveLocation guidance:");
                promptBuilder.AppendLine("- Use title for the saved location name.");
                promptBuilder.AppendLine("- Rely on currentLocation or coordinates in the user request.");
                break;
            case AssistantActionTypeDto.FindNearbyPlaces:
                promptBuilder.AppendLine("FindNearbyPlaces guidance:");
                promptBuilder.AppendLine("- Put only the place type or search term in query, for example cafe, restaurant, or pharmacy.");
                promptBuilder.AppendLine("- Do not put coordinates or pseudo tool text in responseText.");
                promptBuilder.AppendLine("- Rely on currentLocation or coordinates already present in the request.");
                break;
            case AssistantActionTypeDto.CalculateDistance:
                promptBuilder.AppendLine("CalculateDistance guidance:");
                promptBuilder.AppendLine("- Keep the destination wording or coordinates in query or description.");
                promptBuilder.AppendLine("- Rely on currentLocation when the user asks for distance from where they are.");
                break;
        }

        promptBuilder.AppendLine();
        promptBuilder.AppendLine($"Current UTC time: {promptContext.CurrentDateTimeUtc:u}");
        promptBuilder.AppendLine($"Requested at UTC: {request.RequestedAtUtc.ToUniversalTime():u}");

        if (request.CurrentLocation != null)
        {
            promptBuilder.AppendLine(
                $"Current location: latitude {request.CurrentLocation.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}, longitude {request.CurrentLocation.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");
        }
        else
        {
            promptBuilder.AppendLine("Current location: not supplied.");
        }

        promptBuilder.AppendLine();
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

    private async Task<AssistantToolActionResult> TryRecoverPseudoToolActionAsync(
        AssistantCommandRequest request,
        Guid interactionId,
        string responseText,
        CancellationToken cancellationToken)
    {
        ParsedPseudoToolAction? parsedAction = TryParsePseudoToolAction(responseText);

        if (parsedAction == null)
        {
            return AssistantToolActionResult.NotHandled;
        }

        AssistantCommandRequest executableRequest = parsedAction.LocationOverride == null
            ? request
            : request with { CurrentLocation = parsedAction.LocationOverride };

        logger.LogWarning(
            "Attempting to recover pseudo tool syntax from the free-form LLM as action {ActionType}.",
            parsedAction.Action.ActionType);

        return await assistantToolService.ExecutePlannedActionAsync(
            new AssistantPlannedToolActionRequest(
                executableRequest,
                interactionId,
                parsedAction.Action),
            cancellationToken);
    }

    private static ParsedPseudoToolAction? TryParsePseudoToolAction(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return null;
        }

        foreach (Match match in RecoverablePseudoToolCallRegex.Matches(responseText.Trim()))
        {
            string toolName = NormalizePseudoToolName(match.Groups["tool"].Value);
            IReadOnlyDictionary<string, string> arguments = ParsePseudoToolArguments(match.Groups["args"].Value);
            GeoCoordinateDto? locationOverride = TryParsePseudoToolLocation(arguments);
            AssistantPlannedToolAction? action = toolName switch
            {
                "create note" => CreatePlannedToolAction(
                    AssistantActionTypeDto.CreateNote,
                    title: GetPseudoToolArgument(arguments, "title", "name"),
                    description: GetPseudoToolArgument(arguments, "description", "content", "body", "text")),
                "create task" => CreatePlannedToolAction(
                    AssistantActionTypeDto.CreateTask,
                    title: GetPseudoToolArgument(arguments, "title", "name"),
                    description: GetPseudoToolArgument(arguments, "description", "content", "body", "text"),
                    scheduledAt: GetPseudoToolArgument(arguments, "scheduledAt", "dueAt", "when"),
                    priority: TryParseTaskPriority(GetPseudoToolArgument(arguments, "priority")),
                    createLinkedReminder: TryParsePseudoToolBoolean(GetPseudoToolArgument(arguments, "createLinkedReminder", "createReminder"))),
                "create reminder" => CreatePlannedToolAction(
                    AssistantActionTypeDto.CreateReminder,
                    title: GetPseudoToolArgument(arguments, "title", "name"),
                    description: GetPseudoToolArgument(arguments, "description", "message", "content", "body", "text"),
                    scheduledAt: GetPseudoToolArgument(arguments, "scheduledAt", "when"),
                    reminderAt: GetPseudoToolArgument(arguments, "reminderAt", "triggerAt", "when"),
                    reminderTitle: GetPseudoToolArgument(arguments, "reminderTitle", "title", "name"),
                    reminderMessage: GetPseudoToolArgument(arguments, "reminderMessage", "message", "description", "content")),
                "create memory" => CreatePlannedToolAction(
                    AssistantActionTypeDto.CreateMemory,
                    title: GetPseudoToolArgument(arguments, "title", "name"),
                    description: GetPseudoToolArgument(arguments, "description", "content", "body", "text"),
                    tags: GetPseudoToolArgument(arguments, "tags"),
                    isPinned: TryParsePseudoToolBoolean(GetPseudoToolArgument(arguments, "isPinned", "pinned"))),
                "create mileage entry" or "log mileage" => CreatePlannedToolAction(
                    AssistantActionTypeDto.CreateMileageEntry,
                    description: BuildPseudoMileageDescription(arguments)),
                "find nearby places" => CreatePlannedToolAction(
                    AssistantActionTypeDto.FindNearbyPlaces,
                    query: GetPseudoToolArgument(arguments, "query", "type", "search", "place", "category")),
                "save location" => CreatePlannedToolAction(
                    AssistantActionTypeDto.SaveLocation,
                    title: GetPseudoToolArgument(arguments, "title", "name", "label")),
                "calculate distance" => CreatePlannedToolAction(
                    AssistantActionTypeDto.CalculateDistance,
                    query: BuildPseudoDistanceQuery(arguments)),
                "search" => CreatePlannedToolAction(
                    AssistantActionTypeDto.Search,
                    query: GetPseudoToolArgument(arguments, "query", "text", "search")),
                _ => null
            };

            if (action != null)
            {
                return new ParsedPseudoToolAction(action, locationOverride);
            }
        }

        return null;
    }

    private static string NormalizePseudoToolName(string value)
    {
        string compactName = Regex.Replace(value, @"[\s_]+", string.Empty).ToLowerInvariant();

        return compactName switch
        {
            "createnote" => "create note",
            "createtask" => "create task",
            "createreminder" => "create reminder",
            "creatememory" => "create memory",
            "createmileageentry" => "create mileage entry",
            "findnearbyplaces" => "find nearby places",
            "savelocation" or "savemylocation" or "savethislocation" or "savecurrentlocation" => "save location",
            "calculatedistance" => "calculate distance",
            "geocode" => "geocode",
            "reversegeocode" => "reverse geocode",
            "logmileage" => "log mileage",
            "search" => "search",
            _ => NormalizeIntentInput(value).ToLowerInvariant()
        };
    }

    private static IReadOnlyDictionary<string, string> ParsePseudoToolArguments(string value)
    {
        Dictionary<string, string> arguments = new(StringComparer.OrdinalIgnoreCase);

        foreach (string segment in SplitPseudoToolArguments(value))
        {
            int separatorIndex = segment.IndexOf('=');

            if (separatorIndex <= 0)
            {
                continue;
            }

            string key = segment[..separatorIndex].Trim();
            string parsedValue = TrimWrappingQuotes(segment[(separatorIndex + 1)..].Trim());

            if (!string.IsNullOrWhiteSpace(key))
            {
                arguments[key] = parsedValue;
            }
        }

        return arguments;
    }

    private static IEnumerable<string> SplitPseudoToolArguments(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        StringBuilder currentSegment = new();
        char activeQuote = '\0';

        foreach (char character in value)
        {
            if (activeQuote != '\0')
            {
                currentSegment.Append(character);

                if (character == activeQuote)
                {
                    activeQuote = '\0';
                }

                continue;
            }

            if (character == '"' || character == '\'')
            {
                activeQuote = character;
                currentSegment.Append(character);
                continue;
            }

            if (character == ',')
            {
                string segment = currentSegment.ToString().Trim();

                if (!string.IsNullOrWhiteSpace(segment))
                {
                    yield return segment;
                }

                currentSegment.Clear();
                continue;
            }

            currentSegment.Append(character);
        }

        string finalSegment = currentSegment.ToString().Trim();

        if (!string.IsNullOrWhiteSpace(finalSegment))
        {
            yield return finalSegment;
        }
    }

    private static string TrimWrappingQuotes(string value)
    {
        return value.Length >= 2 &&
               ((value[0] == '"' && value[^1] == '"') ||
                (value[0] == '\'' && value[^1] == '\''))
            ? value[1..^1]
            : value;
    }

    private static string? GetPseudoToolArgument(
        IReadOnlyDictionary<string, string> arguments,
        params string[] keys)
    {
        foreach (string key in keys)
        {
            if (arguments.TryGetValue(key, out string? value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static GeoCoordinateDto? TryParsePseudoToolLocation(
        IReadOnlyDictionary<string, string> arguments)
    {
        double? latitude = TryParsePseudoToolDouble(
            GetPseudoToolArgument(arguments, "lat", "latitude"));
        double? longitude = TryParsePseudoToolDouble(
            GetPseudoToolArgument(arguments, "long", "lng", "lon", "longitude"));

        return latitude.HasValue && longitude.HasValue
            ? new GeoCoordinateDto(latitude.Value, longitude.Value)
            : null;
    }

    private static double? TryParsePseudoToolDouble(string? value)
    {
        return double.TryParse(value, out double parsedValue)
            ? parsedValue
            : double.TryParse(
                value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out parsedValue)
                ? parsedValue
                : null;
    }

    private static bool TryParsePseudoToolBoolean(string? value)
    {
        return bool.TryParse(value, out bool parsedValue) && parsedValue;
    }

    private static string? BuildPseudoMileageDescription(IReadOnlyDictionary<string, string> arguments)
    {
        string? odometer = GetPseudoToolArgument(arguments, "odometer", "odometerKm", "reading", "value", "mileage");
        string? trip = GetPseudoToolArgument(arguments, "trip", "tripDistance", "distance");

        if (string.IsNullOrWhiteSpace(odometer))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(trip)
            ? odometer
            : $"{odometer} trip {trip}";
    }

    private static string? BuildPseudoDistanceQuery(IReadOnlyDictionary<string, string> arguments)
    {
        string? from = GetPseudoToolArgument(arguments, "from", "origin");
        string? to = GetPseudoToolArgument(arguments, "to", "destination");

        if (!string.IsNullOrWhiteSpace(from) && !string.IsNullOrWhiteSpace(to))
        {
            return $"{from} to {to}";
        }

        return GetPseudoToolArgument(arguments, "query", "destination", "to");
    }

    private static TaskPriorityDto? TryParseTaskPriority(string? value)
    {
        return Enum.TryParse<TaskPriorityDto>(value, true, out TaskPriorityDto parsedPriority)
            ? parsedPriority
            : null;
    }

    private static AssistantPlannedToolAction CreatePlannedToolAction(
        AssistantActionTypeDto actionType,
        string? title = null,
        string? description = null,
        string? tags = null,
        string? query = null,
        string? scheduledAt = null,
        string? endsAt = null,
        string? timeZoneId = null,
        TaskPriorityDto? priority = null,
        bool isPinned = false,
        bool createLinkedReminder = false,
        string? reminderAt = null,
        string? reminderTitle = null,
        string? reminderMessage = null,
        string? responseText = null)
    {
        return new AssistantPlannedToolAction(
            actionType,
            title,
            description,
            tags,
            query,
            scheduledAt,
            endsAt,
            timeZoneId,
            priority,
            isPinned,
            createLinkedReminder,
            reminderAt,
            reminderTitle,
            reminderMessage,
            responseText);
    }

    private static string NormalizeFallbackResponseText(string responseText, string input)
    {
        string normalizedResponseText = NormalizeResponseText(responseText);

        if (!ShouldGuardAgainstFalseActionClaims(input))
        {
            return normalizedResponseText;
        }

        if (PseudoToolCallRegex.IsMatch(normalizedResponseText))
        {
            return "I could not complete that NOAH action. Please try again, or ask in a more direct way.";
        }

        if (!FalseActionClaimRegex.IsMatch(normalizedResponseText))
        {
            return normalizedResponseText;
        }

        return "I could not confirm that NOAH executed that action. Please try again, or use a more direct command.";
    }

    private static bool ShouldGuardAgainstFalseActionClaims(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        string normalizedInput = NormalizeIntentInput(input);
        bool looksLikeToolIntent = GetExplicitStructuredActionHint(normalizedInput).HasValue ||
                                   StructuredActionIntentRegex.IsMatch(normalizedInput);

        if (looksLikeToolIntent)
        {
            return true;
        }

        return !LooksLikeInformationQuestion(normalizedInput);
    }

    private static bool LooksLikeInformationQuestion(string input)
    {
        return Regex.IsMatch(
            input,
            @"^(?:what|which|who|when|where|why|how)\b",
            RegexOptions.IgnoreCase);
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

        if (ExplicitMemoryIntentRegex.IsMatch(normalizedInput))
        {
            return AssistantActionTypeDto.CreateMemory;
        }

        if (ExplicitSaveLocationIntentRegex.IsMatch(normalizedInput))
        {
            return AssistantActionTypeDto.SaveLocation;
        }

        if (ExplicitNearbyIntentRegex.IsMatch(normalizedInput))
        {
            return AssistantActionTypeDto.FindNearbyPlaces;
        }

        if (ExplicitDistanceIntentRegex.IsMatch(normalizedInput))
        {
            return AssistantActionTypeDto.CalculateDistance;
        }

        if (ExplicitMileageIntentRegex.IsMatch(normalizedInput))
        {
            return AssistantActionTypeDto.CreateMileageEntry;
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

    private sealed record ParsedPseudoToolAction(
        AssistantPlannedToolAction Action,
        GeoCoordinateDto? LocationOverride);

    private async Task<IReadOnlyList<AssistantConversationMemoryEntry>> LoadConversationMemoryAsync(
        Guid currentInteractionId,
        Guid? chatId,
        int take,
        CancellationToken cancellationToken)
    {
        if (take <= 0)
        {
            return Array.Empty<AssistantConversationMemoryEntry>();
        }

        IReadOnlyList<AssistantInteraction> recentCompletedInteractions =
            await assistantInteractionRepository.GetRecentCompletedForScopeAsync(
                chatId,
                take,
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
