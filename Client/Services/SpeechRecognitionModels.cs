namespace Client.Services;

/// <summary>
/// Represents the result of one speech-to-text capture.
/// </summary>
public sealed record SpeechRecognitionResult(
    bool IsSuccessful,
    bool WasCancelled,
    string? Text,
    string? ErrorMessage);
