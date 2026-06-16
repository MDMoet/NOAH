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
    /// Attempts to execute a direct NOAH utility action for the user message.
    /// </summary>
    /// <param name="request">The action request containing the command and interaction id.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The action result, or a not-handled result when semantic planning or normal LLM answering should continue.</returns>
    Task<AssistantToolActionResult> TryExecuteAsync(
        AssistantToolActionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a structured tool plan produced by the LLM.
    /// </summary>
    /// <param name="request">The structured tool action request to execute.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The action result, or a not-handled result when the plan could not be executed.</returns>
    Task<AssistantToolActionResult> ExecutePlannedActionAsync(
        AssistantPlannedToolActionRequest request,
        CancellationToken cancellationToken = default);
}
