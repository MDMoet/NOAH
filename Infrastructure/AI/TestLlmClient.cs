using Application.Interfaces;
using Application.Models;

namespace Infrastructure.AI;

/// <summary>
/// Temporary LLM client used while real model integration is not configured.
/// </summary>
public sealed class TestLlmClient : ILlmClient
{
    /// <summary>
    /// Generates a deterministic test response for the supplied input.
    /// </summary>
    /// <param name="request">The prompt request to echo.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A completed task containing the test response metadata.</returns>
    public Task<LlmChatCompletionResult> GenerateResponseAsync(
        LlmChatCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new LlmChatCompletionResult(
            request.PrimaryModelKey,
            "test-echo",
            $"NOAH received: {ExtractUserMessage(request.Prompt)}",
            "stop",
            false,
            new LlmTokenUsage(null, null, null)));
    }

    /// <summary>
    /// Reports a healthy placeholder status for the deterministic test client.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A completed task containing a single healthy model status.</returns>
    public Task<IReadOnlyList<LlmModelHealthStatus>> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<LlmModelHealthStatus>>(
        [
            new LlmModelHealthStatus(
                "test",
                true,
                true,
                "The deterministic test client is available.",
                "test-echo",
                TimeSpan.Zero)
        ]);
    }

    private static string ExtractUserMessage(string input)
    {
        const string marker = "User message:";

        int markerIndex = input.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);

        return markerIndex < 0
            ? input
            : input[(markerIndex + marker.Length)..].Trim();
    }
}
