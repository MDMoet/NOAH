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
    TimeProvider timeProvider,
    ILogger<AssistantService> logger)
    : IAssistantService
{
    private const string GenericFailureResponseText =
        "Something went wrong while processing the assistant request.";

    private const string CancelledResponseText =
        "The assistant request was cancelled.";

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

        // Store the received interaction before calling tools or the LLM so failed requests remain traceable.
        await assistantInteractionRepository.AddAsync(assistantInteraction, cancellationToken);

        try
        {
            AssistantToolActionResult toolActionResult = await assistantToolService.TryExecuteAsync(
                new AssistantToolActionRequest(request with { Input = input, RequestedAtUtc = requestedAtUtc }, assistantInteraction.Id),
                cancellationToken);

            if (toolActionResult.WasHandled)
            {
                assistantInteraction.ActionType = (AssistantActionType)toolActionResult.ActionType;
                assistantInteraction.AssistantResponse = NormalizeResponseText(toolActionResult.ResponseText);
                assistantInteraction.RelatedEntityId = toolActionResult.RelatedEntityId;
                assistantInteraction.RelatedEntityType = toolActionResult.RelatedEntityType;
                assistantInteraction.Status = AssistantInteractionStatus.Completed;
                assistantInteraction.CompletedAtUtc = timeProvider.GetUtcNow();
                assistantInteraction.UpdatedAtUtc = assistantInteraction.CompletedAtUtc;

                await assistantInteractionRepository.UpdateAsync(assistantInteraction, cancellationToken);

                logger.LogInformation(
                    "Assistant request {InteractionId} was handled by tool action {ActionType}.",
                    assistantInteraction.Id,
                    toolActionResult.ActionType);

                return MapToResponse(assistantInteraction);
            }

            AssistantPromptContext promptContext =
                await assistantToolService.BuildContextAsync(
                    request with { Input = input, RequestedAtUtc = requestedAtUtc },
                    cancellationToken);
            string prompt = assistantPromptBuilder.BuildPrompt(request with { Input = input, RequestedAtUtc = requestedAtUtc }, promptContext);

            logger.LogInformation(
                "Assistant request {InteractionId} fell back to LLM processing with {SearchResultCount} contextual search result(s).",
                assistantInteraction.Id,
                promptContext.SearchResults.Count);

            string responseText = await llmClient.GenerateResponseAsync(prompt, cancellationToken);

            assistantInteraction.AssistantResponse = NormalizeResponseText(responseText);
            assistantInteraction.Status = AssistantInteractionStatus.Completed;
            assistantInteraction.CompletedAtUtc = timeProvider.GetUtcNow();
            assistantInteraction.UpdatedAtUtc = assistantInteraction.CompletedAtUtc;

            await assistantInteractionRepository.UpdateAsync(assistantInteraction, cancellationToken);

            logger.LogInformation(
                "Assistant request {InteractionId} completed through the LLM path.",
                assistantInteraction.Id);

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
                "Assistant request {InteractionId} was cancelled while processing input: {Input}",
                assistantInteraction.Id,
                input);

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
                "Error processing assistant request {InteractionId}: {Input}",
                assistantInteraction.Id,
                input);

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
