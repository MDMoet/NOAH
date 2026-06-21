namespace Client.Services;

/// <summary>
/// Fallback speech service used on platforms without an implementation.
/// </summary>
public sealed class UnsupportedSpeechToTextService : ISpeechToTextService
{
    public bool IsSupported => false;

    public Task<SpeechRecognitionResult> ListenOnceAsync(string culture, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new SpeechRecognitionResult(
            false,
            false,
            null,
            "Speech recognition is not supported on this platform."));
    }
}
