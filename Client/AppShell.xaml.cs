namespace Client;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute("assistant", typeof(MainPage));
    }
}
