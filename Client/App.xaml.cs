using Client.Pages;
using Client.Services;

namespace Client;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        BootstrapPage bootstrapPage = ServiceHelper.GetRequiredService<BootstrapPage>();
        return new Window(bootstrapPage);
    }
}
