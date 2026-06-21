namespace Client.Services;

public sealed class UserDialogService
{
    public async Task<string?> PromptAsync(
        string title,
        string message,
        string? initialValue = null,
        string accept = "Save",
        string cancel = "Cancel")
    {
        Page? page = ResolvePage();

        if (page == null)
        {
            return null;
        }

        return await page.DisplayPromptAsync(
            title,
            message,
            accept,
            cancel,
            initialValue: initialValue);
    }

    public async Task<bool> ConfirmAsync(
        string title,
        string message,
        string accept = "Yes",
        string cancel = "No")
    {
        Page? page = ResolvePage();

        if (page == null)
        {
            return false;
        }

        return await page.DisplayAlertAsync(
            title,
            message,
            accept,
            cancel);
    }

    public async Task ShowAlertAsync(
        string title,
        string message,
        string cancel = "OK")
    {
        Page? page = ResolvePage();

        if (page == null)
        {
            return;
        }

        await page.DisplayAlertAsync(title, message, cancel);
    }

    public async Task<string?> ShowActionSheetAsync(
        string title,
        string cancel,
        string? destructionButton = null,
        params string[] buttons)
    {
        Page? page = ResolvePage();

        if (page == null)
        {
            return null;
        }

        return await page.DisplayActionSheetAsync(
            title,
            cancel,
            destructionButton,
            buttons);
    }

    private static Page? ResolvePage()
    {
        Page? shellPage = Shell.Current?.CurrentPage;

        if (shellPage != null)
        {
            return shellPage;
        }

        return Application.Current?.Windows.FirstOrDefault()?.Page;
    }
}
