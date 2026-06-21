using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Client.Views.Components;

/// <summary>
/// Shared rounded input used for search and short text entry throughout the NOAH client.
/// </summary>
public partial class SearchEntryView : ContentView
{
    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(SearchEntryView), default(string), BindingMode.TwoWay);

    public static readonly BindableProperty PlaceholderProperty =
        BindableProperty.Create(nameof(Placeholder), typeof(string), typeof(SearchEntryView), string.Empty);

    public static readonly BindableProperty IconSourceProperty =
        BindableProperty.Create(nameof(IconSource), typeof(ImageSource), typeof(SearchEntryView));

    public static readonly BindableProperty ShowIconProperty =
        BindableProperty.Create(nameof(ShowIcon), typeof(bool), typeof(SearchEntryView), true, propertyChanged: OnIconLayoutPropertyChanged);

    public static readonly BindableProperty ReturnCommandProperty =
        BindableProperty.Create(nameof(ReturnCommand), typeof(ICommand), typeof(SearchEntryView));

    public static readonly BindableProperty FontSizeProperty =
        BindableProperty.Create(nameof(FontSize), typeof(double), typeof(SearchEntryView), 13d);

    public static readonly BindableProperty EntryHeightProperty =
        BindableProperty.Create(nameof(EntryHeight), typeof(double), typeof(SearchEntryView), 44d);

    public static readonly BindableProperty ControlHeightProperty =
        BindableProperty.Create(nameof(ControlHeight), typeof(double), typeof(SearchEntryView), 52d);

    public static readonly BindableProperty IconWidthProperty =
        BindableProperty.Create(nameof(IconWidth), typeof(double), typeof(SearchEntryView), 18d);

    public static readonly BindableProperty IconHeightProperty =
        BindableProperty.Create(nameof(IconHeight), typeof(double), typeof(SearchEntryView), 18d);

    public static readonly BindableProperty IconOpacityProperty =
        BindableProperty.Create(nameof(IconOpacity), typeof(double), typeof(SearchEntryView), 0.85d);

    public static readonly BindableProperty FieldPaddingProperty =
        BindableProperty.Create(nameof(FieldPadding), typeof(Thickness), typeof(SearchEntryView), new Thickness(18, 0));

    public static readonly BindableProperty ColumnSpacingProperty =
        BindableProperty.Create(nameof(ColumnSpacing), typeof(double), typeof(SearchEntryView), 12d, propertyChanged: OnIconLayoutPropertyChanged);

    public static readonly BindableProperty CornerRadiusProperty =
        BindableProperty.Create(nameof(CornerRadius), typeof(double), typeof(SearchEntryView), 999d, propertyChanged: OnCornerRadiusPropertyChanged);

    public static readonly BindableProperty FieldBackgroundColorProperty =
        BindableProperty.Create(nameof(FieldBackgroundColor), typeof(Color), typeof(SearchEntryView), Color.FromArgb("#17122B"));

    public static readonly BindableProperty FieldBorderColorProperty =
        BindableProperty.Create(nameof(FieldBorderColor), typeof(Color), typeof(SearchEntryView), Colors.Transparent);

    public static readonly BindableProperty FieldBorderThicknessProperty =
        BindableProperty.Create(nameof(FieldBorderThickness), typeof(double), typeof(SearchEntryView), 0d);

    public static readonly BindableProperty PlaceholderColorProperty =
        BindableProperty.Create(nameof(PlaceholderColor), typeof(Color), typeof(SearchEntryView), Color.FromArgb("#8A7FA0"));

    public static readonly BindableProperty EntryTextColorProperty =
        BindableProperty.Create(nameof(EntryTextColor), typeof(Color), typeof(SearchEntryView), Color.FromArgb("#E8E4F0"));

    public SearchEntryView()
    {
        InitializeComponent();
        UpdateStrokeShape();
        UpdateIconLayout();
    }

    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public ImageSource? IconSource
    {
        get => (ImageSource?)GetValue(IconSourceProperty);
        set => SetValue(IconSourceProperty, value);
    }

    public bool ShowIcon
    {
        get => (bool)GetValue(ShowIconProperty);
        set => SetValue(ShowIconProperty, value);
    }

    public ICommand? ReturnCommand
    {
        get => (ICommand?)GetValue(ReturnCommandProperty);
        set => SetValue(ReturnCommandProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public double EntryHeight
    {
        get => (double)GetValue(EntryHeightProperty);
        set => SetValue(EntryHeightProperty, value);
    }

    public double ControlHeight
    {
        get => (double)GetValue(ControlHeightProperty);
        set => SetValue(ControlHeightProperty, value);
    }

    public double IconWidth
    {
        get => (double)GetValue(IconWidthProperty);
        set => SetValue(IconWidthProperty, value);
    }

    public double IconHeight
    {
        get => (double)GetValue(IconHeightProperty);
        set => SetValue(IconHeightProperty, value);
    }

    public double IconOpacity
    {
        get => (double)GetValue(IconOpacityProperty);
        set => SetValue(IconOpacityProperty, value);
    }

    public Thickness FieldPadding
    {
        get => (Thickness)GetValue(FieldPaddingProperty);
        set => SetValue(FieldPaddingProperty, value);
    }

    public double ColumnSpacing
    {
        get => (double)GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public Color FieldBackgroundColor
    {
        get => (Color)GetValue(FieldBackgroundColorProperty);
        set => SetValue(FieldBackgroundColorProperty, value);
    }

    public Color FieldBorderColor
    {
        get => (Color)GetValue(FieldBorderColorProperty);
        set => SetValue(FieldBorderColorProperty, value);
    }

    public double FieldBorderThickness
    {
        get => (double)GetValue(FieldBorderThicknessProperty);
        set => SetValue(FieldBorderThicknessProperty, value);
    }

    public Color PlaceholderColor
    {
        get => (Color)GetValue(PlaceholderColorProperty);
        set => SetValue(PlaceholderColorProperty, value);
    }

    public Color EntryTextColor
    {
        get => (Color)GetValue(EntryTextColorProperty);
        set => SetValue(EntryTextColorProperty, value);
    }

    private static void OnCornerRadiusPropertyChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        ((SearchEntryView)bindable).UpdateStrokeShape();
    }

    private static void OnIconLayoutPropertyChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        ((SearchEntryView)bindable).UpdateIconLayout();
    }

    private void UpdateStrokeShape()
    {
        if (FieldBorder is null)
        {
            return;
        }

        FieldBorder.StrokeShape = new RoundRectangle
        {
            CornerRadius = new CornerRadius(CornerRadius)
        };
    }

    private void UpdateIconLayout()
    {
        if (FieldLayout is null)
        {
            return;
        }

        FieldLayout.ColumnSpacing = ShowIcon ? ColumnSpacing : 0d;
    }
}
