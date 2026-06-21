namespace Client.Behaviors;

public class AdaptiveStateBehavior : Behavior<Grid>
{
    public static readonly BindableProperty BreakpointProperty = BindableProperty.Create(
        nameof(Breakpoint), typeof(double), typeof(AdaptiveStateBehavior), 720.0);

    public static readonly BindableProperty MobileStateProperty = BindableProperty.Create(
        nameof(MobileState), typeof(string), typeof(AdaptiveStateBehavior), "MobileState");

    public static readonly BindableProperty DesktopStateProperty = BindableProperty.Create(
        nameof(DesktopState), typeof(string), typeof(AdaptiveStateBehavior), "DesktopState");

    public double Breakpoint
    {
        get => (double)GetValue(BreakpointProperty);
        set => SetValue(BreakpointProperty, value);
    }

    public string MobileState
    {
        get => (string)GetValue(MobileStateProperty)!;
        set => SetValue(MobileStateProperty, value);
    }

    public string DesktopState
    {
        get => (string)GetValue(DesktopStateProperty)!;
        set => SetValue(DesktopStateProperty, value);
    }

    private string? _currentState;
    private Grid? _associatedObject;

    protected override void OnAttachedTo(Grid bindable)
    {
        base.OnAttachedTo(bindable);
        _associatedObject = bindable;
        bindable.SizeChanged += OnSizeChanged;
        // initialize state
        UpdateState(bindable.Width);
    }

    protected override void OnDetachingFrom(Grid bindable)
    {
        base.OnDetachingFrom(bindable);
        bindable.SizeChanged -= OnSizeChanged;
        _associatedObject = null;
    }

    private void OnSizeChanged(object? sender, EventArgs e)
    {
        if (sender is Grid grid)
        {
            UpdateState(grid.Width);
        }
    }

    private void UpdateState(double width)
    {
        var targetState = width >= Breakpoint ? DesktopState : MobileState;
        if (_currentState == targetState)
            return;

        _currentState = targetState;

        if (_associatedObject == null)
            return;

        VisualStateManager.GoToState(_associatedObject, targetState);
    }
}
