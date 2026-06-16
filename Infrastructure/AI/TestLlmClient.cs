using Application.Interfaces;

namespace Infrastructure.AI;

/// <summary>
/// Temporary LLM client used while real model integration is not configured.
/// </summary>
public sealed class TestLlmClient : ILlmClient
{
    /// <summary>
    /// Generates a deterministic test response for the supplied input.
    /// </summary>
    /// <param name="input">The prompt or user input to echo.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A completed task containing the test response text.</returns>
    public Task<string> GenerateResponseAsync(string input, CancellationToken cancellationToken = default)
    {
        return Task.FromResult($"NOAH received: {ExtractUserMessage(input)}");
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
