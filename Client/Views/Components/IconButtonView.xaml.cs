using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Client.Views.Components;

/// <summary>
/// Reusable icon-only button for navigation and quick actions.
/// </summary>
public partial class IconButtonView : ContentView
{
    public static readonly BindableProperty IconSourceProperty =
        BindableProperty.Create(nameof(IconSource), typeof(ImageSource), typeof(IconButtonView));

    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(IconButtonView));

    public static readonly BindableProperty IsSelectedProperty =
        BindableProperty.Create(nameof(IsSelected), typeof(bool), typeof(IconButtonView), false, propertyChanged: OnAppearancePropertyChanged);

    public static readonly BindableProperty ButtonPaddingProperty =
        BindableProperty.Create(nameof(ButtonPadding), typeof(Thickness), typeof(IconButtonView), new Thickness(0));

    public static readonly BindableProperty IconWidthProperty =
        BindableProperty.Create(nameof(IconWidth), typeof(double), typeof(IconButtonView), 22d, propertyChanged: OnAppearancePropertyChanged);

    public static readonly BindableProperty IconHeightProperty =
        BindableProperty.Create(nameof(IconHeight), typeof(double), typeof(IconButtonView), 22d, propertyChanged: OnAppearancePropertyChanged);

    public static readonly BindableProperty IconOpacityProperty =
        BindableProperty.Create(nameof(IconOpacity), typeof(double), typeof(IconButtonView), 0.7d, propertyChanged: OnAppearancePropertyChanged);

    public static readonly BindableProperty SelectedIconOpacityProperty =
        BindableProperty.Create(nameof(SelectedIconOpacity), typeof(double), typeof(IconButtonView), 1d, propertyChanged: OnAppearancePropertyChanged);

    public static readonly BindableProperty SelectedScaleProperty =
        BindableProperty.Create(nameof(SelectedScale), typeof(double), typeof(IconButtonView), 1.08d, propertyChanged: OnAppearancePropertyChanged);

    public static readonly BindableProperty SelectedShadowColorProperty =
        BindableProperty.Create(nameof(SelectedShadowColor), typeof(Color), typeof(IconButtonView), Color.FromArgb("#8B5CF6"), propertyChanged: OnAppearancePropertyChanged);

    public static readonly BindableProperty SelectedShadowRadiusProperty =
        BindableProperty.Create(nameof(SelectedShadowRadius), typeof(float), typeof(IconButtonView), 18f, propertyChanged: OnAppearancePropertyChanged);

    public static readonly BindableProperty SelectedShadowOpacityProperty =
        BindableProperty.Create(nameof(SelectedShadowOpacity), typeof(float), typeof(IconButtonView), 0f, propertyChanged: OnAppearancePropertyChanged);

    public static readonly BindableProperty CornerRadiusProperty =
        BindableProperty.Create(nameof(CornerRadius), typeof(double), typeof(IconButtonView), 10d, propertyChanged: OnCornerRadiusPropertyChanged);

    public static readonly BindableProperty NormalFillColorProperty =
        BindableProperty.Create(nameof(NormalFillColor), typeof(Color), typeof(IconButtonView), Colors.Transparent, propertyChanged: OnAppearancePropertyChanged);

    public static readonly BindableProperty NormalBorderColorProperty =
        BindableProperty.Create(nameof(NormalBorderColor), typeof(Color), typeof(IconButtonView), Colors.Transparent, propertyChanged: OnAppearancePropertyChanged);

    public static readonly BindableProperty NormalBorderThicknessProperty =
        BindableProperty.Create(nameof(NormalBorderThickness), typeof(double), typeof(IconButtonView), 0d, propertyChanged: OnAppearancePropertyChanged);

    public static readonly BindableProperty SelectedFillColorProperty =
        BindableProperty.Create(nameof(SelectedFillColor), typeof(Color), typeof(IconButtonView), Colors.Transparent, propertyChanged: OnAppearancePropertyChanged);

    public static readonly BindableProperty SelectedBorderColorProperty =
        BindableProperty.Create(nameof(SelectedBorderColor), typeof(Color), typeof(IconButtonView), Colors.Transparent, propertyChanged: OnAppearancePropertyChanged);

    public static readonly BindableProperty SelectedBorderThicknessProperty =
        BindableProperty.Create(nameof(SelectedBorderThickness), typeof(double), typeof(IconButtonView), 0d, propertyChanged: OnAppearancePropertyChanged);

    public IconButtonView()
    {
        InitializeComponent();
        UpdateStrokeShape();
        UpdateAppearance();
    }

    public ImageSource? IconSource
    {
        get => (ImageSource?)GetValue(IconSourceProperty);
        set => SetValue(IconSourceProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public Thickness ButtonPadding
    {
        get => (Thickness)GetValue(ButtonPaddingProperty);
        set => SetValue(ButtonPaddingProperty, value);
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

    public double SelectedIconOpacity
    {
        get => (double)GetValue(SelectedIconOpacityProperty);
        set => SetValue(SelectedIconOpacityProperty, value);
    }

    public double SelectedScale
    {
        get => (double)GetValue(SelectedScaleProperty);
        set => SetValue(SelectedScaleProperty, value);
    }

    public Color SelectedShadowColor
    {
        get => (Color)GetValue(SelectedShadowColorProperty);
        set => SetValue(SelectedShadowColorProperty, value);
    }

    public float SelectedShadowRadius
    {
        get => (float)GetValue(SelectedShadowRadiusProperty);
        set => SetValue(SelectedShadowRadiusProperty, value);
    }

    public float SelectedShadowOpacity
    {
        get => (float)GetValue(SelectedShadowOpacityProperty);
        set => SetValue(SelectedShadowOpacityProperty, value);
    }

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public Color NormalFillColor
    {
        get => (Color)GetValue(NormalFillColorProperty);
        set => SetValue(NormalFillColorProperty, value);
    }

    public Color NormalBorderColor
    {
        get => (Color)GetValue(NormalBorderColorProperty);
        set => SetValue(NormalBorderColorProperty, value);
    }

    public double NormalBorderThickness
    {
        get => (double)GetValue(NormalBorderThicknessProperty);
        set => SetValue(NormalBorderThicknessProperty, value);
    }

    public Color SelectedFillColor
    {
        get => (Color)GetValue(SelectedFillColorProperty);
        set => SetValue(SelectedFillColorProperty, value);
    }

    public Color SelectedBorderColor
    {
        get => (Color)GetValue(SelectedBorderColorProperty);
        set => SetValue(SelectedBorderColorProperty, value);
    }

    public double SelectedBorderThickness
    {
        get => (double)GetValue(SelectedBorderThicknessProperty);
        set => SetValue(SelectedBorderThicknessProperty, value);
    }

    private static void OnAppearancePropertyChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        ((IconButtonView)bindable).UpdateAppearance();
    }

    private static void OnCornerRadiusPropertyChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        ((IconButtonView)bindable).UpdateStrokeShape();
    }

    private void UpdateAppearance()
    {
        if (ButtonBorder is null || IconImage is null)
        {
            return;
        }

        var fillColor = IsSelected ? SelectedFillColor : NormalFillColor;
        var borderColor = IsSelected ? SelectedBorderColor : NormalBorderColor;
        var borderThickness = IsSelected ? SelectedBorderThickness : NormalBorderThickness;

        ButtonBorder.BackgroundColor = fillColor;
        ButtonBorder.Stroke = new SolidColorBrush(borderColor);
        ButtonBorder.StrokeThickness = borderThickness;

        IconImage.Opacity = IsSelected ? SelectedIconOpacity : IconOpacity;
        IconImage.Scale = IsSelected ? SelectedScale : 1d;
        IconImage.Shadow = IsSelected && SelectedShadowOpacity > 0
            ? new Shadow
            {
                Brush = new SolidColorBrush(SelectedShadowColor),
                Offset = new Point(0, 0),
                Radius = SelectedShadowRadius,
                Opacity = SelectedShadowOpacity
            }
            : new Shadow
            {
                Brush = new SolidColorBrush(Colors.Transparent),
                Offset = new Point(0, 0),
                Radius = 0,
                Opacity = 0
            };
    }

    private void UpdateStrokeShape()
    {
        if (ButtonBorder is null)
        {
            return;
        }

        ButtonBorder.StrokeShape = new RoundRectangle
        {
            CornerRadius = new CornerRadius(CornerRadius)
        };
    }
}
