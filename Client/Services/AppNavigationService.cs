using Client.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;

namespace Client.Services;

/// <summary>
/// Swaps the root page between the login flow and the authenticated shell.
/// </summary>
public sealed class AppNavigationService(IServiceProvider serviceProvider)
{
    public Task ShowBootstrapAsync()
    {
        return SetRootPageAsync(serviceProvider.GetRequiredService<BootstrapPage>());
    }

    public Task ShowLoginAsync()
    {
        return SetRootPageAsync(serviceProvider.GetRequiredService<LoginPage>());
    }

    public Task ShowMainShellAsync()
    {
        return SetRootPageAsync(serviceProvider.GetRequiredService<AppShell>());
    }

    public Task NavigateHomeSectionAsync(string? section = null)
    {
        string route = string.IsNullOrWhiteSpace(section)
            ? "//HomePage"
            : $"//HomePage?section={Uri.EscapeDataString(section)}";

        return Shell.Current.GoToAsync(route);
    }

    public Task NavigateAssistantAsync()
    {
        return Shell.Current.GoToAsync("//assistant");
    }

    private static Task SetRootPageAsync(Page page)
    {
        TaskCompletionSource<bool> taskCompletionSource = new();

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                Window? window = Application.Current?.Windows.FirstOrDefault();

                if (window == null)
                {
                    throw new InvalidOperationException("NOAH could not access the application window.");
                }

                window.Page = page;
                taskCompletionSource.SetResult(true);
            }
            catch (Exception exception)
            {
                taskCompletionSource.SetException(exception);
            }
        });

        return taskCompletionSource.Task;
    }
}
