using System.Windows.Input;
using Microsoft.Maui.Controls;

namespace Client.Behaviors;

/// <summary>
/// Adds a native long-press command to a MAUI view.
/// </summary>
public sealed class LongPressBehavior : Behavior<View>
{
    public static readonly BindableProperty CommandProperty = BindableProperty.Create(
        nameof(Command),
        typeof(ICommand),
        typeof(LongPressBehavior));

    public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(
        nameof(CommandParameter),
        typeof(object),
        typeof(LongPressBehavior));

    private View? associatedObject;

#if ANDROID
    private Android.Views.View? platformView;
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

    protected override void OnAttachedTo(View bindable)
    {
        base.OnAttachedTo(bindable);
        associatedObject = bindable;
        bindable.HandlerChanged += OnHandlerChanged;
        bindable.HandlerChanging += OnHandlerChanging;
        AttachToPlatformView(bindable.Handler);
    }

    protected override void OnDetachingFrom(View bindable)
    {
        DetachFromPlatformView();
        bindable.HandlerChanged -= OnHandlerChanged;
        bindable.HandlerChanging -= OnHandlerChanging;
        associatedObject = null;
        base.OnDetachingFrom(bindable);
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
        if (Command?.CanExecute(CommandParameter) == true)
        {
            Command.Execute(CommandParameter);
        }
    }

    private void AttachToPlatformView(IElementHandler? handler)
    {
#if ANDROID
        DetachFromPlatformView();

        if (handler?.PlatformView is Android.Views.View nativeView)
        {
            platformView = nativeView;
            platformView.Clickable = true;
            platformView.Focusable = true;
            platformView.LongClickable = true;
            platformView.LongClick += OnPlatformViewLongClick;
        }
#endif
    }

    private void DetachFromPlatformView()
    {
#if ANDROID
        if (platformView == null)
        {
            return;
        }

        platformView.LongClick -= OnPlatformViewLongClick;
        platformView = null;
#endif
    }

#if ANDROID
    private void OnPlatformViewLongClick(object? sender, Android.Views.View.LongClickEventArgs e)
    {
        ExecuteCommand();
        e.Handled = true;
    }
#endif
}
