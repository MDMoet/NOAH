using Application.Models;
using NOAH.Contracts.Assistant;

namespace Application.Interfaces;

/// <summary>
/// Provides assistant-accessible NOAH tools and contextual lookups.
/// </summary>
public interface IAssistantToolService
{
    /// <summary>
    /// Builds contextual data for a user message before LLM execution.
    /// </summary>
    /// <param name="request">The assistant command request.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The prompt context for the message.</returns>
    Task<AssistantPromptContext> BuildContextAsync(
        AssistantCommandRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to execute a concrete NOAH tool action for the user message.
    /// </summary>
    /// <param name="request">The action request containing the command and interaction id.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The action result, or a not-handled result when the LLM should answer instead.</returns>
    Task<AssistantToolActionResult> TryExecuteAsync(
        AssistantToolActionRequest request,
        CancellationToken cancellationToken = default);
}
