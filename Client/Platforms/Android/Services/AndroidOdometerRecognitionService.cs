using System.Collections;
using Android.Gms.Extensions;
using Android.Graphics;
using Xamarin.Google.MLKit.Vision.Common;
using Xamarin.Google.MLKit.Vision.Text;
using Xamarin.Google.MLKit.Vision.Text.Latin;

namespace Client.Services;

/// <summary>
/// Uses Google ML Kit on Android to read an odometer photo.
/// </summary>
public sealed class AndroidOdometerRecognitionService : IOdometerRecognitionService
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
            cancellationToken.ThrowIfCancellationRequested();

            using Bitmap? bitmap = BitmapFactory.DecodeFile(imagePath);
            if (bitmap == null)
            {
                return new OdometerRecognitionResult(false, null, string.Empty, [], "NOAH could not open the captured image.");
            }

            InputImage inputImage = InputImage.FromBitmap(bitmap, 0);
            var recognizer = TextRecognition.GetClient(TextRecognizerOptions.DefaultOptions);
            object recognizedText = await recognizer.Process(inputImage);
            List<OdometerTextObservation> observations = ExtractObservations(recognizedText);

            return OdometerRecognitionParser.Parse(observations);
        }
        catch (System.Exception exception)
        {
            return new OdometerRecognitionResult(false, null, string.Empty, [], $"Odometer OCR failed: {exception.Message}");
        }
    }

    private static List<OdometerTextObservation> ExtractObservations(object recognizedText)
    {
        List<OdometerTextObservation> observations = [];

        foreach (object block in EnumerateProperty(recognizedText, "TextBlocks"))
        {
            foreach (object line in EnumerateProperty(block, "Lines"))
            {
                Android.Graphics.Rect? boundingBox = GetPropertyValue<Android.Graphics.Rect>(line, "BoundingBox");
                string lineText = GetPropertyValue<string>(line, "Text") ?? string.Empty;

                observations.Add(new OdometerTextObservation(
                    lineText,
                    boundingBox?.Width() ?? 0,
                    boundingBox?.Height() ?? 0,
                    boundingBox?.Left ?? 0,
                    boundingBox?.Top ?? 0));
            }
        }

        return observations;
    }

    private static IEnumerable<object> EnumerateProperty(object instance, string propertyName)
    {
        if (instance.GetType().GetProperty(propertyName)?.GetValue(instance) is not IEnumerable values)
        {
            yield break;
        }

        foreach (object? value in values)
        {
            if (value != null)
            {
                yield return value;
            }
        }
    }

    private static T? GetPropertyValue<T>(object instance, string propertyName)
    {
        object? value = instance.GetType().GetProperty(propertyName)?.GetValue(instance);

        if (value is T typedValue)
        {
            return typedValue;
        }

        return default;
    }
}
