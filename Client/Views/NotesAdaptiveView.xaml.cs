using Client.ViewModels;

namespace Client.Views;

public partial class NotesAdaptiveView : ContentView
{
    public NotesAdaptiveView()
    {
        InitializeComponent();
    }

    public NotesAdaptiveView(HomeViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        _ = viewModel.LoadDataAsync();
    }
}
