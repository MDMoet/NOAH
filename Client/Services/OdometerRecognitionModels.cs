namespace Client.Services;

/// <summary>
/// Describes one OCR line used while selecting the most likely odometer reading.
/// </summary>
public sealed record OdometerTextObservation(
    string Text,
    double Width,
    double Height,
    double Left,
    double Top);

/// <summary>
/// Describes one numeric candidate extracted from OCR output.
/// </summary>
public sealed record OdometerRecognitionCandidate(
    string RawText,
    double ValueKm,
    double Score);

/// <summary>
/// Represents the parsed OCR output for an odometer photo.
/// </summary>
public sealed record OdometerRecognitionResult(
    bool IsSuccessful,
    double? OdometerKm,
    string RecognizedText,
    IReadOnlyList<OdometerRecognitionCandidate> Candidates,
    string? ErrorMessage);
