using Client.ViewModels;

namespace Client.Views;

public partial class HomeAdaptiveView : ContentView
{
    public HomeAdaptiveView()
    {
        InitializeComponent();
    }

    public HomeAdaptiveView(HomeViewModel viewModel)
    {
        InitializeComponent();

        BindingContext = viewModel;

        _ = viewModel.LoadDataAsync();
    }
}
