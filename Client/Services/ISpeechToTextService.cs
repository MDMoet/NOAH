namespace Client.Services;

/// <summary>
/// Captures one spoken utterance and returns it as text.
/// </summary>
public interface ISpeechToTextService
{
    /// <summary>
    /// Indicates whether speech recognition is supported on the current platform.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Starts listening once and resolves when final text is recognized.
    /// </summary>
    /// <param name="culture">The requested speech culture, such as en-US.</param>
    /// <param name="cancellationToken">Token used to cancel the recognition session.</param>
    /// <returns>The recognition result.</returns>
    Task<SpeechRecognitionResult> ListenOnceAsync(string culture, CancellationToken cancellationToken = default);
}
