using Application.Interfaces;
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
    IAssistantInteractionRepository assistantInteractionRepository)
    : IAssistantService
{
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

        DateTimeOffset currentDateTimeUtc = DateTimeOffset.UtcNow;
        string input = request.Input.Trim();
        AssistantResponseModeDto responseMode = request.PreferredResponseMode ?? AssistantResponseModeDto.Text;

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
            RequestedAtUtc = request.RequestedAtUtc.ToUniversalTime(),
            CompletedAtUtc = null,
            CreatedAtUtc = currentDateTimeUtc,
            UpdatedAtUtc = null
        };

        // Store the received interaction before calling the LLM so failed requests remain traceable.
        await assistantInteractionRepository.AddAsync(assistantInteraction, cancellationToken);

        try
        {
            string responseText = await llmClient.GenerateResponseAsync(input, cancellationToken);

            assistantInteraction.AssistantResponse = responseText;
            assistantInteraction.Status = AssistantInteractionStatus.Completed;
            assistantInteraction.CompletedAtUtc = DateTimeOffset.UtcNow;
            assistantInteraction.UpdatedAtUtc = assistantInteraction.CompletedAtUtc;

            await assistantInteractionRepository.UpdateAsync(assistantInteraction, cancellationToken);

            return MapToResponse(assistantInteraction);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            DateTimeOffset failedAtUtc = DateTimeOffset.UtcNow;

            assistantInteraction.Status = AssistantInteractionStatus.Failed;
            assistantInteraction.ErrorMessage = exception.Message;
            assistantInteraction.AssistantResponse = "Something went wrong while processing the assistant request.";
            assistantInteraction.CompletedAtUtc = failedAtUtc;
            assistantInteraction.UpdatedAtUtc = failedAtUtc;

            await assistantInteractionRepository.UpdateAsync(assistantInteraction, cancellationToken);

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
            assistantInteraction.AssistantResponse ?? string.Empty,
            (AssistantResponseModeDto)assistantInteraction.ResponseMode,
            assistantInteraction.RelatedEntityId,
            assistantInteraction.RelatedEntityType);
    }
}
