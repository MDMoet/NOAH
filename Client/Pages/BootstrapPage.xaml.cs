using Client.Services;

namespace Client.Pages;

public partial class BootstrapPage : ContentPage
{
    private readonly NoahAuthenticationService authenticationService;
    private readonly AppNavigationService navigationService;
    private bool hasCheckedStartup;

    public BootstrapPage(
        NoahAuthenticationService authenticationService,
        AppNavigationService navigationService)
    {
        InitializeComponent();
        this.authenticationService = authenticationService;
        this.navigationService = navigationService;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (hasCheckedStartup)
        {
            return;
        }

        hasCheckedStartup = true;

        try
        {
            if (await authenticationService.HasAccessAsync())
            {
                await navigationService.ShowMainShellAsync();
                return;
            }
        }
        catch
        {
            // Fall back to the login screen when secure storage or startup checks are unavailable.
        }

        await navigationService.ShowLoginAsync();
    }
}
