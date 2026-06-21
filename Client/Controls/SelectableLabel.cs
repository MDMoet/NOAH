using Microsoft.Maui.Controls;

namespace Client.Controls;

/// <summary>
/// A label that opts into native text-selection support where the platform allows it.
/// </summary>
public sealed class SelectableLabel : Label
{
    public static readonly BindableProperty EnableSelectionProperty = BindableProperty.Create(
        nameof(EnableSelection),
        typeof(bool),
        typeof(SelectableLabel),
        true);

    public bool EnableSelection
    {
        get => (bool)GetValue(EnableSelectionProperty);
        set => SetValue(EnableSelectionProperty, value);
    }
}
