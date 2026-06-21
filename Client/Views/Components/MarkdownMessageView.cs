using Client.Models;
using Microsoft.Maui.Controls;

namespace Client.Views.Components;

/// <summary>
/// Renders chat markdown into native MAUI views so the content stays selectable and themed.
/// </summary>
public sealed class MarkdownMessageView : ContentView
{
    public static readonly BindableProperty MarkdownProperty = BindableProperty.Create(
        nameof(Markdown),
        typeof(string),
        typeof(MarkdownMessageView),
        string.Empty,
        propertyChanged: OnRenderablePropertyChanged);

    public static readonly BindableProperty BodyFontSizeProperty = BindableProperty.Create(
        nameof(BodyFontSize),
        typeof(double),
        typeof(MarkdownMessageView),
        16d,
        propertyChanged: OnRenderablePropertyChanged);

    private readonly VerticalStackLayout contentStack = new()
    {
        Spacing = 0
    };

    public MarkdownMessageView()
    {
        Content = contentStack;
    }

    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public double BodyFontSize
    {
        get => (double)GetValue(BodyFontSizeProperty);
        set => SetValue(BodyFontSizeProperty, value);
    }

    private static void OnRenderablePropertyChanged(BindableObject bindable, object? _, object? __)
    {
        ((MarkdownMessageView)bindable).Rebuild();
    }

    private void Rebuild()
    {
        contentStack.Children.Clear();

        foreach (View view in ChatMarkdownFormatter.CreateViews(Markdown, BodyFontSize))
        {
            contentStack.Children.Add(view);
        }
    }
}
