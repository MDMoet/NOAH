using Client.Controls;
using Client.Pages;
using Client.Services;
using Client.ViewModels;
using Client.Views;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Handlers;

#if ANDROID
using Android.Content.Res;
#endif

#if WINDOWS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
#endif

namespace Client;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        ConfigureInputChrome();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton(_ => CreateNoahHttpClient());
        builder.Services.AddSingleton<AssistantApiSettingsService>();
        builder.Services.AddSingleton<NoahAuthenticationService>();
        builder.Services.AddSingleton<AppNavigationService>();
        builder.Services.AddSingleton<AssistantApiService>();
        builder.Services.AddSingleton<NoahApiClient>();
        builder.Services.AddSingleton<UserDialogService>();
        builder.Services.AddSingleton<IUserLocationService, DeviceLocationService>();
        builder.Services.AddSingleton<INoteRepository, ApiNoteRepository>();
        builder.Services.AddSingleton<ITaskRepository, ApiTaskRepository>();
        builder.Services.AddSingleton<IReminderRepository, ApiReminderRepository>();
        builder.Services.AddSingleton<IMileageRepository, ApiMileageRepository>();
        builder.Services.AddSingleton<IAiChatService, AssistantQuickChatService>();

#if ANDROID
        builder.Services.AddSingleton<ISpeechToTextService, AndroidSpeechToTextService>();
        builder.Services.AddSingleton<IOdometerRecognitionService, AndroidOdometerRecognitionService>();
#elif WINDOWS
        builder.Services.AddSingleton<ISpeechToTextService, WindowsSpeechToTextService>();
        builder.Services.AddSingleton<IOdometerRecognitionService, WindowsOdometerRecognitionService>();
#else
        builder.Services.AddSingleton<ISpeechToTextService, UnsupportedSpeechToTextService>();
        builder.Services.AddSingleton<IOdometerRecognitionService, UnsupportedOdometerRecognitionService>();
#endif

        builder.Services.AddSingleton<CalendarViewModel>();
        builder.Services.AddTransient<HomeViewModel>();
        builder.Services.AddTransient<MainPageViewModel>();
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<HomePage>();
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<BootstrapPage>();
        builder.Services.AddTransient<AppShell>();
        builder.Services.AddTransient<HomeAdaptiveView>();
        builder.Services.AddTransient<NotesAdaptiveView>();
        builder.Services.AddTransient<SettingsModalView>();
        builder.Services.AddTransient<CalendarAdaptiveView>();

        MauiApp app = builder.Build();
        ServiceHelper.Services = app.Services;
        return app;
    }

    private static HttpClient CreateNoahHttpClient()
    {
        HttpClientHandler handler = new()
        {
            ServerCertificateCustomValidationCallback = (request, _, _, sslErrors) =>
            {
                if (sslErrors == System.Net.Security.SslPolicyErrors.None)
                {
                    return true;
                }

                string? host = request?.RequestUri?.Host;
                return string.Equals(host, "100.74.230.23", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase);
            }
        };

        return new HttpClient(handler);
    }

    private static void ConfigureInputChrome()
    {
        // Chat messages are rendered as native labels so we can keep full text selection and copy support.
        LabelHandler.Mapper.AppendToMapping("NoahSelectableLabel", (handler, view) =>
        {
            if (view is not SelectableLabel { EnableSelection: true })
            {
                return;
            }

#if ANDROID
            handler.PlatformView.SetTextIsSelectable(true);
            handler.PlatformView.LongClickable = true;
            handler.PlatformView.Focusable = true;
            handler.PlatformView.FocusableInTouchMode = true;
#elif WINDOWS
            handler.PlatformView.IsTextSelectionEnabled = true;
#endif
        });

        EntryHandler.Mapper.AppendToMapping("NoahBorderlessEntry", (handler, _) =>
        {
#if ANDROID
            handler.PlatformView.Background = null;
            handler.PlatformView.SetBackgroundColor(Android.Graphics.Color.Transparent);
            handler.PlatformView.BackgroundTintList = ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
            handler.PlatformView.SetPadding(0, 0, 0, 0);
#elif WINDOWS
            handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
            handler.PlatformView.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            handler.PlatformView.Padding = new Microsoft.UI.Xaml.Thickness(0);
#endif
        });

        EditorHandler.Mapper.AppendToMapping("NoahBorderlessEditor", (handler, _) =>
        {
#if ANDROID
            handler.PlatformView.Background = null;
            handler.PlatformView.SetBackgroundColor(Android.Graphics.Color.Transparent);
            handler.PlatformView.BackgroundTintList = ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
            handler.PlatformView.SetPadding(0, 0, 0, 0);
#elif WINDOWS
            handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
            handler.PlatformView.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            handler.PlatformView.Padding = new Microsoft.UI.Xaml.Thickness(0);
#endif
        });

        DatePickerHandler.Mapper.AppendToMapping("NoahBorderlessDatePicker", (handler, _) =>
        {
#if ANDROID
            handler.PlatformView.Background = null;
            handler.PlatformView.SetBackgroundColor(Android.Graphics.Color.Transparent);
            handler.PlatformView.BackgroundTintList = ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
            handler.PlatformView.SetPadding(0, 0, 0, 0);
#elif WINDOWS
            handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
            handler.PlatformView.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            handler.PlatformView.Padding = new Microsoft.UI.Xaml.Thickness(0);
#endif
        });

        TimePickerHandler.Mapper.AppendToMapping("NoahBorderlessTimePicker", (handler, _) =>
        {
#if ANDROID
            handler.PlatformView.Background = null;
            handler.PlatformView.SetBackgroundColor(Android.Graphics.Color.Transparent);
            handler.PlatformView.BackgroundTintList = ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
            handler.PlatformView.SetPadding(0, 0, 0, 0);
#elif WINDOWS
            handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
            handler.PlatformView.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            handler.PlatformView.Padding = new Microsoft.UI.Xaml.Thickness(0);
#endif
        });

        PickerHandler.Mapper.AppendToMapping("NoahBorderlessPicker", (handler, _) =>
        {
#if ANDROID
            handler.PlatformView.Background = null;
            handler.PlatformView.SetBackgroundColor(Android.Graphics.Color.Transparent);
            handler.PlatformView.BackgroundTintList = ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
            handler.PlatformView.SetPadding(0, 0, 0, 0);
#elif WINDOWS
            handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
            handler.PlatformView.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            handler.PlatformView.Padding = new Microsoft.UI.Xaml.Thickness(0);
#endif
        });
    }
}
