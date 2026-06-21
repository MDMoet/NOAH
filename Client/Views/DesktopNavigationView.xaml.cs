using System.Windows.Input;

namespace Client.Views;

public partial class DesktopNavigationView : ContentView
{
    public static readonly BindableProperty IsHomeSelectedProperty =
        BindableProperty.Create(nameof(IsHomeSelected), typeof(bool), typeof(DesktopNavigationView), false);

    public static readonly BindableProperty IsChatSelectedProperty =
        BindableProperty.Create(nameof(IsChatSelected), typeof(bool), typeof(DesktopNavigationView), false);

    public static readonly BindableProperty IsNotesSelectedProperty =
        BindableProperty.Create(nameof(IsNotesSelected), typeof(bool), typeof(DesktopNavigationView), false);

    public static readonly BindableProperty IsCalendarSelectedProperty =
        BindableProperty.Create(nameof(IsCalendarSelected), typeof(bool), typeof(DesktopNavigationView), false);

    public static readonly BindableProperty IsMileageSelectedProperty =
        BindableProperty.Create(nameof(IsMileageSelected), typeof(bool), typeof(DesktopNavigationView), false);

    public static readonly BindableProperty ShowTopNavigationProperty =
        BindableProperty.Create(nameof(ShowTopNavigation), typeof(bool), typeof(DesktopNavigationView), true);

    public static readonly BindableProperty ShowSettingsProperty =
        BindableProperty.Create(nameof(ShowSettings), typeof(bool), typeof(DesktopNavigationView), true);

    public static readonly BindableProperty NavigateHomeCommandProperty =
        BindableProperty.Create(nameof(NavigateHomeCommand), typeof(ICommand), typeof(DesktopNavigationView));

    public static readonly BindableProperty NavigateMileageCommandProperty =
        BindableProperty.Create(nameof(NavigateMileageCommand), typeof(ICommand), typeof(DesktopNavigationView));

    public static readonly BindableProperty NavigateNotesCommandProperty =
        BindableProperty.Create(nameof(NavigateNotesCommand), typeof(ICommand), typeof(DesktopNavigationView));

    public static readonly BindableProperty NavigateCalendarCommandProperty =
        BindableProperty.Create(nameof(NavigateCalendarCommand), typeof(ICommand), typeof(DesktopNavigationView));

    public static readonly BindableProperty NavigateChatCommandProperty =
        BindableProperty.Create(nameof(NavigateChatCommand), typeof(ICommand), typeof(DesktopNavigationView));

    public static readonly BindableProperty ToggleSettingsCommandProperty =
        BindableProperty.Create(nameof(ToggleSettingsCommand), typeof(ICommand), typeof(DesktopNavigationView));

    public DesktopNavigationView()
    {
        InitializeComponent();
    }

    public bool IsHomeSelected
    {
        get => (bool)GetValue(IsHomeSelectedProperty);
        set => SetValue(IsHomeSelectedProperty, value);
    }

    public bool IsChatSelected
    {
        get => (bool)GetValue(IsChatSelectedProperty);
        set => SetValue(IsChatSelectedProperty, value);
    }

    public bool IsNotesSelected
    {
        get => (bool)GetValue(IsNotesSelectedProperty);
        set => SetValue(IsNotesSelectedProperty, value);
    }

    public bool IsCalendarSelected
    {
        get => (bool)GetValue(IsCalendarSelectedProperty);
        set => SetValue(IsCalendarSelectedProperty, value);
    }

    public bool IsMileageSelected
    {
        get => (bool)GetValue(IsMileageSelectedProperty);
        set => SetValue(IsMileageSelectedProperty, value);
    }

    public bool ShowTopNavigation
    {
        get => (bool)GetValue(ShowTopNavigationProperty);
        set => SetValue(ShowTopNavigationProperty, value);
    }

    public bool ShowSettings
    {
        get => (bool)GetValue(ShowSettingsProperty);
        set => SetValue(ShowSettingsProperty, value);
    }

    public ICommand? NavigateHomeCommand
    {
        get => (ICommand?)GetValue(NavigateHomeCommandProperty);
        set => SetValue(NavigateHomeCommandProperty, value);
    }

    public ICommand? NavigateMileageCommand
    {
        get => (ICommand?)GetValue(NavigateMileageCommandProperty);
        set => SetValue(NavigateMileageCommandProperty, value);
    }

    public ICommand? NavigateNotesCommand
    {
        get => (ICommand?)GetValue(NavigateNotesCommandProperty);
        set => SetValue(NavigateNotesCommandProperty, value);
    }

    public ICommand? NavigateCalendarCommand
    {
        get => (ICommand?)GetValue(NavigateCalendarCommandProperty);
        set => SetValue(NavigateCalendarCommandProperty, value);
    }

    public ICommand? NavigateChatCommand
    {
        get => (ICommand?)GetValue(NavigateChatCommandProperty);
        set => SetValue(NavigateChatCommandProperty, value);
    }

    public ICommand? ToggleSettingsCommand
    {
        get => (ICommand?)GetValue(ToggleSettingsCommandProperty);
        set => SetValue(ToggleSettingsCommandProperty, value);
    }
}
