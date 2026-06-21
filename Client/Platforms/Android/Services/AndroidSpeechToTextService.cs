using Android.Content;
using Android.OS;
using Android.Speech;
using Microsoft.Maui.ApplicationModel;

namespace Client.Services;

/// <summary>
/// Uses Android's native speech recognizer to capture one spoken utterance.
/// </summary>
public sealed class AndroidSpeechToTextService : ISpeechToTextService
{
    private TaskCompletionSource<SpeechRecognitionResult>? recognitionCompletionSource;
    private SpeechRecognizer? speechRecognizer;
    private RecognitionListener? recognitionListener;
    private CancellationTokenRegistration cancellationRegistration;

    public bool IsSupported => SpeechRecognizer.IsRecognitionAvailable(Platform.AppContext);

    public async Task<SpeechRecognitionResult> ListenOnceAsync(
        string culture,
        CancellationToken cancellationToken = default)
    {
        PermissionStatus permissionStatus = await Permissions.RequestAsync<Permissions.Microphone>();

        if (permissionStatus != PermissionStatus.Granted)
        {
            return new SpeechRecognitionResult(false, false, null, "Microphone access was denied.");
        }

        if (!IsSupported)
        {
            return new SpeechRecognitionResult(false, false, null, "Speech recognition is not available on this device.");
        }

        if (recognitionCompletionSource != null)
        {
            return new SpeechRecognitionResult(false, false, null, "Speech recognition is already active.");
        }

        recognitionCompletionSource = new TaskCompletionSource<SpeechRecognitionResult>();
        cancellationRegistration = cancellationToken.Register(CancelRecognition);

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            Context context = Platform.CurrentActivity ?? Platform.AppContext;
            speechRecognizer = SpeechRecognizer.CreateSpeechRecognizer(context);

            if (speechRecognizer == null)
            {
                Finish(new SpeechRecognitionResult(false, false, null, "Speech recognition could not be started on this device."));
                return;
            }

            recognitionListener = new RecognitionListener(Finish);
            speechRecognizer.SetRecognitionListener(recognitionListener);

            Intent intent = new(RecognizerIntent.ActionRecognizeSpeech);
            intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
            intent.PutExtra(RecognizerIntent.ExtraPartialResults, false);
            intent.PutExtra(RecognizerIntent.ExtraCallingPackage, context.PackageName ?? string.Empty);

            if (!string.IsNullOrWhiteSpace(culture))
            {
                intent.PutExtra(RecognizerIntent.ExtraLanguage, culture);
                intent.PutExtra(RecognizerIntent.ExtraLanguagePreference, culture);
            }

            speechRecognizer.StartListening(intent);
        });

        return await recognitionCompletionSource.Task;
    }

    private void CancelRecognition()
    {
        MainThread.BeginInvokeOnMainThread(() => speechRecognizer?.Cancel());
        Finish(new SpeechRecognitionResult(false, true, null, null));
    }

    private void Finish(SpeechRecognitionResult result)
    {
        TaskCompletionSource<SpeechRecognitionResult>? completionSource = recognitionCompletionSource;

        if (completionSource == null)
        {
            return;
        }

        recognitionCompletionSource = null;
        cancellationRegistration.Dispose();

        SpeechRecognizer? recognizer = speechRecognizer;
        speechRecognizer = null;
        recognitionListener = null;

        if (recognizer != null)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                recognizer.StopListening();
                recognizer.Destroy();
            });
        }

        completionSource.TrySetResult(result);
    }

    private sealed class RecognitionListener(Action<SpeechRecognitionResult> finish)
        : global::Java.Lang.Object, IRecognitionListener
    {
        public void OnReadyForSpeech(Bundle? @params)
        {
        }

        public void OnBeginningOfSpeech()
        {
        }

        public void OnRmsChanged(float rmsdB)
        {
        }

        public void OnBufferReceived(byte[]? buffer)
        {
        }

        public void OnEndOfSpeech()
        {
        }

        public void OnError(SpeechRecognizerError error)
        {
            SpeechRecognitionResult result = error switch
            {
                SpeechRecognizerError.NoMatch => new SpeechRecognitionResult(
                    false,
                    false,
                    null,
                    "NOAH could not make out the sentence."),
                SpeechRecognizerError.SpeechTimeout => new SpeechRecognitionResult(
                    false,
                    false,
                    null,
                    "NOAH stopped listening because nothing was said."),
                SpeechRecognizerError.Client => new SpeechRecognitionResult(
                    false,
                    true,
                    null,
                    null),
                _ => new SpeechRecognitionResult(
                    false,
                    false,
                    null,
                    $"Speech recognition failed ({error}).")
            };

            finish(result);
        }

        public void OnResults(Bundle? results)
        {
            string? text = results?
                .GetStringArrayList(SpeechRecognizer.ResultsRecognition)?
                .FirstOrDefault(match => !string.IsNullOrWhiteSpace(match))
                ?.Trim();

            finish(string.IsNullOrWhiteSpace(text)
                ? new SpeechRecognitionResult(false, false, null, "NOAH could not hear anything.")
                : new SpeechRecognitionResult(true, false, text, null));
        }

        public void OnPartialResults(Bundle? partialResults)
        {
        }

        public void OnEvent(int eventType, Bundle? @params)
        {
        }
    }
}
