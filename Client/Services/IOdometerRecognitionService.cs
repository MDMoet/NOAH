namespace Client.Services;

/// <summary>
/// Extracts the most likely odometer value from a captured dashboard photo.
/// </summary>
public interface IOdometerRecognitionService
{
    /// <summary>
    /// Indicates whether OCR is supported on the current platform.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Runs OCR and odometer parsing for the supplied image path.
    /// </summary>
    /// <param name="imagePath">The local image path to inspect.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The recognition result.</returns>
    Task<OdometerRecognitionResult> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default);
}
