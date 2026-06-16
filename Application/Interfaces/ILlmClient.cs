namespace Application.Interfaces;

/// <summary>
/// Represents a client capable of generating assistant responses from prompts.
/// </summary>
public interface ILlmClient
{
    /// <summary>
    /// Generates a response for the supplied prompt.
    /// </summary>
    /// <param name="prompt">The prompt to send to the language model.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The generated response text.</returns>
    Task<string> GenerateResponseAsync(string prompt, CancellationToken cancellationToken = default);
}
