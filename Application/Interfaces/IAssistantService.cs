using NOAH.Contracts.Assistant;

namespace Application.Interfaces;

/// <summary>
/// Coordinates assistant message processing and interaction persistence.
/// </summary>
public interface IAssistantService
{
    /// <summary>
    /// Processes a user message through the assistant.
    /// </summary>
    /// <param name="request">The assistant command request to process.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The assistant response and interaction metadata.</returns>
    Task<AssistantCommandResponse> ProcessMessageAsync(AssistantCommandRequest request, CancellationToken cancellationToken = default);
}
