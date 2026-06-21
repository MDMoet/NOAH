namespace Client.Behaviors;

public class TapScaleBehavior : Behavior<View>
{
    private TapGestureRecognizer? _tapGestureRecognizer;

    protected override void OnAttachedTo(View bindable)
    {
        base.OnAttachedTo(bindable);

        _tapGestureRecognizer = new TapGestureRecognizer();
        _tapGestureRecognizer.Tapped += OnTapped;
        bindable.GestureRecognizers.Add(_tapGestureRecognizer);
    }

    protected override void OnDetachingFrom(View bindable)
    {
        if (_tapGestureRecognizer != null)
        {
            _tapGestureRecognizer.Tapped -= OnTapped;
            bindable.GestureRecognizers.Remove(_tapGestureRecognizer);
        }

        base.OnDetachingFrom(bindable);
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not VisualElement element)
            return;

        _ = AnimateTapAsync(element);
    }

    private static async Task AnimateTapAsync(VisualElement element)
    {
        try
        {
            await element.ScaleToAsync(0.94, 70, Easing.CubicOut);
            await element.ScaleToAsync(1, 110, Easing.CubicOut);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }
}
