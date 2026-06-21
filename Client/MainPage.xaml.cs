using Client.ViewModels;

namespace Client;

public partial class MainPage : ContentPage
{
    private readonly MainPageViewModel viewModel;
    private bool isInitialized;
    private bool isDrawerPanTriggered;

    public MainPage(MainPageViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        viewModel.PrepareForNavigation();

        if (isInitialized)
        {
            return;
        }

        isInitialized = true;
        await viewModel.InitializeAsync();
    }

    private void OnMobileDrawerPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                isDrawerPanTriggered = false;
                break;

            case GestureStatus.Running:
                if (!isDrawerPanTriggered && e.TotalX >= 56)
                {
                    isDrawerPanTriggered = true;
                    viewModel.OpenDrawer();
                }

                break;

            case GestureStatus.Canceled:
            case GestureStatus.Completed:
                isDrawerPanTriggered = false;
                break;
        }
    }
}
