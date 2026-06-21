namespace Client.Services;

/// <summary>
/// Fallback odometer OCR service used on unsupported platforms.
/// </summary>
public sealed class UnsupportedOdometerRecognitionService : IOdometerRecognitionService
{
    public bool IsSupported => false;

    public Task<OdometerRecognitionResult> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new OdometerRecognitionResult(
            false,
            null,
            string.Empty,
            [],
            "Odometer OCR is not supported on this platform."));
    }
}
