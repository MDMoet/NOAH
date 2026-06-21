using Windows.Foundation;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

namespace Client.Services;

/// <summary>
/// Uses the Windows OCR engine to read an odometer photo.
/// </summary>
public sealed class WindowsOdometerRecognitionService : IOdometerRecognitionService
{
    public bool IsSupported => true;

    public async Task<OdometerRecognitionResult> RecognizeAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return new OdometerRecognitionResult(false, null, string.Empty, [], "The captured image could not be found.");
        }

        try
        {
            StorageFile storageFile = await StorageFile.GetFileFromPathAsync(imagePath);
            using var stream = await storageFile.OpenReadAsync();
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
            SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            OcrEngine engine = OcrEngine.TryCreateFromUserProfileLanguages() ?? OcrEngine.TryCreateFromLanguage(new Language("en-US"));

            if (engine == null)
            {
                return new OdometerRecognitionResult(false, null, string.Empty, [], "Windows OCR is unavailable.");
            }

            OcrResult result = await engine.RecognizeAsync(softwareBitmap);
            List<OdometerTextObservation> observations = result.Lines
                .Select(line => new OdometerTextObservation(
                    line.Text ?? string.Empty,
                    GetBoundingRect(line).Width,
                    GetBoundingRect(line).Height,
                    GetBoundingRect(line).X,
                    GetBoundingRect(line).Y))
                .ToList();

            return OdometerRecognitionParser.Parse(observations);
        }
        catch (Exception exception)
        {
            return new OdometerRecognitionResult(false, null, string.Empty, [], $"Odometer OCR failed: {exception.Message}");
        }
    }

    private static Windows.Foundation.Rect GetBoundingRect(OcrLine line)
    {
        if (line.Words.Count == 0)
        {
            return Windows.Foundation.Rect.Empty;
        }

        double left = line.Words.Min(word => word.BoundingRect.X);
        double top = line.Words.Min(word => word.BoundingRect.Y);
        double right = line.Words.Max(word => word.BoundingRect.X + word.BoundingRect.Width);
        double bottom = line.Words.Max(word => word.BoundingRect.Y + word.BoundingRect.Height);

        return new Windows.Foundation.Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
}
