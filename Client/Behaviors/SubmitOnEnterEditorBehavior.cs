using System.Windows.Input;
using Microsoft.Maui.Controls;

#if WINDOWS
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;
#endif

namespace Client.Behaviors;

/// <summary>
/// Submits a chat editor on Enter while preserving Shift+Enter for new lines.
/// </summary>
public sealed class SubmitOnEnterEditorBehavior : Behavior<Editor>
{
    private static readonly TimeSpan DuplicateSubmitWindow = TimeSpan.FromMilliseconds(100);

    public static readonly BindableProperty CommandProperty = BindableProperty.Create(
        nameof(Command),
        typeof(ICommand),
        typeof(SubmitOnEnterEditorBehavior));

    public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(
        nameof(CommandParameter),
        typeof(object),
        typeof(SubmitOnEnterEditorBehavior));

    private Editor? associatedObject;
    private DateTimeOffset lastCommandExecutionAt = DateTimeOffset.MinValue;

#if WINDOWS
    private Microsoft.UI.Xaml.Controls.TextBox? platformTextBox;
    private bool allowNextLineBreak;
    private bool suppressTextChanged;
#endif

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    protected override void OnAttachedTo(Editor bindable)
    {
        base.OnAttachedTo(bindable);
        associatedObject = bindable;
        BindingContext = bindable.BindingContext;
        bindable.BindingContextChanged += OnBindingContextChanged;
        bindable.HandlerChanged += OnHandlerChanged;
        bindable.HandlerChanging += OnHandlerChanging;
#if WINDOWS
        bindable.TextChanged += OnEditorTextChanged;
#endif
        AttachToPlatformView(bindable.Handler);
    }

    protected override void OnDetachingFrom(Editor bindable)
    {
        DetachFromPlatformView();
        bindable.BindingContextChanged -= OnBindingContextChanged;
        bindable.HandlerChanged -= OnHandlerChanged;
        bindable.HandlerChanging -= OnHandlerChanging;
#if WINDOWS
        bindable.TextChanged -= OnEditorTextChanged;
#endif
        BindingContext = null;
        associatedObject = null;
        base.OnDetachingFrom(bindable);
    }

    private void OnBindingContextChanged(object? sender, EventArgs e)
    {
        BindingContext = associatedObject?.BindingContext;
    }

    private void OnHandlerChanged(object? sender, EventArgs e)
    {
        AttachToPlatformView(associatedObject?.Handler);
    }

    private void OnHandlerChanging(object? sender, HandlerChangingEventArgs e)
    {
        DetachFromPlatformView();
    }

    private void ExecuteCommand()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        if (now - lastCommandExecutionAt < DuplicateSubmitWindow)
        {
            return;
        }

        if (Command?.CanExecute(CommandParameter) == true)
        {
            lastCommandExecutionAt = now;
            Command.Execute(CommandParameter);
        }
    }

    private void AttachToPlatformView(IElementHandler? handler)
    {
#if WINDOWS
        DetachFromPlatformView();

        if (handler?.PlatformView is Microsoft.UI.Xaml.Controls.TextBox textBox)
        {
            platformTextBox = textBox;
            platformTextBox.PreviewKeyDown += OnPlatformTextBoxPreviewKeyDown;
            platformTextBox.KeyDown += OnPlatformTextBoxKeyDown;
        }
#endif
    }

    private void DetachFromPlatformView()
    {
#if WINDOWS
        if (platformTextBox == null)
        {
            return;
        }

        platformTextBox.PreviewKeyDown -= OnPlatformTextBoxPreviewKeyDown;
        platformTextBox.KeyDown -= OnPlatformTextBoxKeyDown;
        platformTextBox = null;
#endif
    }

#if WINDOWS
    private void OnPlatformTextBoxPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        HandlePlatformTextBoxKeyDown(e);
    }

    private void OnPlatformTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        HandlePlatformTextBoxKeyDown(e);
    }

    private void HandlePlatformTextBoxKeyDown(KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        if (IsShiftKeyDown())
        {
            allowNextLineBreak = true;
            return;
        }

        allowNextLineBreak = false;
        e.Handled = true;
        ExecuteCommand();
    }

    private void OnEditorTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (suppressTextChanged || associatedObject == null)
        {
            return;
        }

        string newText = e.NewTextValue ?? string.Empty;

        if (!EndsWithLineBreak(newText))
        {
            return;
        }

        if (allowNextLineBreak)
        {
            allowNextLineBreak = false;
            return;
        }

        suppressTextChanged = true;
        associatedObject.Text = RemoveTrailingLineBreak(newText);
        suppressTextChanged = false;
        ExecuteCommand();
    }

    private static bool EndsWithLineBreak(string value)
    {
        return value.EndsWith('\n') || value.EndsWith('\r');
    }

    private static string RemoveTrailingLineBreak(string value)
    {
        if (value.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return value[..^2];
        }

        if (EndsWithLineBreak(value))
        {
            return value[..^1];
        }

        return value;
    }

    private static bool IsShiftKeyDown()
    {
        CoreVirtualKeyStates shiftState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        return (shiftState & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
    }
#endif
}
