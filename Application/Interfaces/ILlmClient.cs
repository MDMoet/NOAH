using Application.Models;

namespace Application.Interfaces;

/// <summary>
/// Represents a client capable of generating free-form and structured assistant responses from prompts.
/// </summary>
public interface ILlmClient
{
    /// <summary>
    /// Generates a response for the supplied prompt.
    /// </summary>
    /// <param name="request">The completion request to send to the language model.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The generated completion result.</returns>
    Task<LlmChatCompletionResult> GenerateResponseAsync(
        LlmChatCompletionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks the health of configured model endpoints.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The per-model health status snapshots.</returns>
    Task<IReadOnlyList<LlmModelHealthStatus>> CheckHealthAsync(CancellationToken cancellationToken = default);
}
