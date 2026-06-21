using Windows.Globalization;
using Windows.Media.SpeechRecognition;

namespace Client.Services;

/// <summary>
/// Uses the Windows speech recognizer to capture one spoken utterance.
/// </summary>
public sealed class WindowsSpeechToTextService : ISpeechToTextService
{
    public bool IsSupported => true;

    public async Task<SpeechRecognitionResult> ListenOnceAsync(
        string culture,
        CancellationToken cancellationToken = default)
    {
        PermissionStatus permissionStatus = await Permissions.RequestAsync<Permissions.Microphone>();

        if (permissionStatus != PermissionStatus.Granted)
        {
            return new SpeechRecognitionResult(false, false, null, "Microphone access was denied.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using SpeechRecognizer recognizer = CreateRecognizer(culture);
            SpeechRecognitionCompilationResult compilationResult = await recognizer.CompileConstraintsAsync();

            if (compilationResult.Status != SpeechRecognitionResultStatus.Success)
            {
                return new SpeechRecognitionResult(false, false, null, $"Speech recognition is unavailable ({compilationResult.Status}).");
            }

            Windows.Media.SpeechRecognition.SpeechRecognitionResult recognitionResult =
                await recognizer.RecognizeAsync();

            return recognitionResult.Status == SpeechRecognitionResultStatus.Success &&
                   !string.IsNullOrWhiteSpace(recognitionResult.Text)
                ? new SpeechRecognitionResult(true, false, recognitionResult.Text.Trim(), null)
                : new SpeechRecognitionResult(
                    false,
                    false,
                    null,
                    $"NOAH could not make out the sentence ({recognitionResult.Status}).");
        }
        catch (OperationCanceledException)
        {
            return new SpeechRecognitionResult(false, true, null, null);
        }
        catch (Exception exception)
        {
            return new SpeechRecognitionResult(false, false, null, $"Speech recognition failed: {exception.Message}");
        }
    }

    private static SpeechRecognizer CreateRecognizer(string culture)
    {
        if (string.IsNullOrWhiteSpace(culture))
        {
            return new SpeechRecognizer();
        }

        try
        {
            return new SpeechRecognizer(new Language(culture));
        }
        catch
        {
            return new SpeechRecognizer();
        }
    }
}
