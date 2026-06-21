using Client.ViewModels;

namespace Client.Views;

public partial class ChatAdaptiveView : ContentView
{
    public ChatAdaptiveView()
    {
        InitializeComponent();
    }

    public ChatAdaptiveView(HomeViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        _ = viewModel.LoadDataAsync();
    }
}
