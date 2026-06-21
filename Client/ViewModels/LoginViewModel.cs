using System.Windows.Input;
using Client.Models;
using Client.Services;

namespace Client.ViewModels;

/// <summary>
/// Drives the demo sign-in screen shown when the client does not have a trusted API key.
/// </summary>
public sealed class LoginViewModel : ObservableObject
{
    private readonly NoahAuthenticationService authenticationService;
    private readonly AppNavigationService navigationService;
    private readonly Command signInCommand;
    private string username = string.Empty;
    private string password = string.Empty;
    private string statusText = string.Empty;
    private bool isBusy;

    public LoginViewModel(
        NoahAuthenticationService authenticationService,
        AppNavigationService navigationService)
    {
        this.authenticationService = authenticationService;
        this.navigationService = navigationService;
        signInCommand = new Command(async () => await SignInAsync(), CanSignIn);
    }

    public ICommand SignInCommand => signInCommand;

    public string Username
    {
        get => username;
        set
        {
            if (SetProperty(ref username, value))
            {
                signInCommand.ChangeCanExecute();
            }
        }
    }

    public string Password
    {
        get => password;
        set
        {
            if (SetProperty(ref password, value))
            {
                signInCommand.ChangeCanExecute();
            }
        }
    }

    public string StatusText
    {
        get => statusText;
        private set
        {
            if (SetProperty(ref statusText, value))
            {
                OnPropertyChanged(nameof(HasStatusText));
            }
        }
    }

    public bool HasStatusText => !string.IsNullOrWhiteSpace(StatusText);

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                signInCommand.ChangeCanExecute();
            }
        }
    }

    private bool CanSignIn()
    {
        return !IsBusy &&
               !string.IsNullOrWhiteSpace(Username) &&
               !string.IsNullOrWhiteSpace(Password);
    }

    private async Task SignInAsync()
    {
        if (!CanSignIn())
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusText = string.Empty;

            await authenticationService.SignInAsync(Username.Trim(), Password);
            Password = string.Empty;
            await navigationService.ShowMainShellAsync();
        }
        catch (Exception exception)
        {
            StatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
