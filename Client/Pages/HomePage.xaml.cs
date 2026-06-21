using Client.ViewModels;

namespace Client.Pages;

public partial class HomePage : ContentPage, IQueryAttributable
{
    private readonly HomeViewModel _viewModel;
    private bool _hasLoadedData;

    public HomePage(HomeViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }
    // Adaptive layout is handled by AdaptiveStateBehavior attached in XAML.

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_hasLoadedData)
            return;

        _hasLoadedData = true;
        await _viewModel.LoadDataAsync();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        string? section = query.TryGetValue("section", out object? value)
            ? value?.ToString()
            : null;

        _viewModel.NavigateToSection(section);
    }
}
