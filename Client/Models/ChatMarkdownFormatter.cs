using System.Net;
using System.Text.RegularExpressions;
using Client.Controls;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Client.Models;

internal static class ChatMarkdownFormatter
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private static readonly Color TextColor = Colors.White;
    private static readonly Color AccentSoftColor = Color.FromArgb("#C4B5FD");
    private static readonly Color QuoteBorderColor = Color.FromArgb("#8B5CF6");
    private static readonly Color DividerColor = Color.FromArgb("#2A2144");
    private static readonly Color InlineCodeBackgroundColor = Color.FromArgb("#17122B");
    private static readonly Color InlineCodeTextColor = Color.FromArgb("#C4B5FD");
    private static readonly Color CodeSurfaceColor = Color.FromArgb("#0E0B17");
    private static readonly Color CodeHeaderColor = Color.FromArgb("#17122B");
    private static readonly Color CodeBorderColor = Color.FromArgb("#32244D");
    private static readonly Color CodeDefaultColor = Color.FromArgb("#D4D4D4");
    private static readonly Color CodeKeywordColor = Color.FromArgb("#C586C0");
    private static readonly Color CodeStringColor = Color.FromArgb("#CE9178");
    private static readonly Color CodeCommentColor = Color.FromArgb("#6A9955");
    private static readonly Color CodeNumberColor = Color.FromArgb("#B5CEA8");
    private static readonly Color CodeTypeColor = Color.FromArgb("#4EC9B0");
    private static readonly Color CodePropertyColor = Color.FromArgb("#9CDCFE");
    private static readonly Color CodeVariableColor = Color.FromArgb("#9CDCFE");

    private static readonly Regex CSharpRegex = new(
        """(?<comment>//.*?$|/\*[\s\S]*?\*/)|(?<string>@\"(?:\"\"|[^\"])*\"|\"(?:\\.|[^\"\\])*\"|'(?:\\.|[^'\\])*')|(?<number>\b\d+(?:\.\d+)?(?:[mMdDfFlLuU]+)?\b)|(?<type>\b(?:string|int|long|short|byte|bool|double|float|decimal|char|object|Guid|DateTime|DateTimeOffset|Task|List|Dictionary|IEnumerable|IReadOnlyList|var)\b)|(?<keyword>\b(?:using|namespace|public|private|protected|internal|static|sealed|partial|class|record|struct|enum|interface|async|await|new|return|if|else|switch|case|for|foreach|while|do|break|continue|try|catch|finally|throw|null|true|false|this|base|get|set|init|where|when|in|out|ref|params)\b)""",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex JsonRegex = new(
        """(?<property>"(?:\\.|[^"\\])*"(?=\s*:))|(?<string>"(?:\\.|[^"\\])*")|(?<number>-?\b\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\b)|(?<keyword>\b(?:true|false|null)\b)""",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex SqlRegex = new(
        """(?<comment>--.*?$|/\*[\s\S]*?\*/)|(?<string>'(?:''|[^'])*')|(?<number>\b\d+(?:\.\d+)?\b)|(?<keyword>\b(?:select|from|where|and|or|insert|into|values|update|set|delete|join|left|right|inner|outer|on|group|by|order|having|top|limit|as|case|when|then|else|end|create|table|alter|drop|primary|key|foreign|not|null|distinct)\b)""",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ScriptRegex = new(
        """(?<comment>//.*?$|/\*[\s\S]*?\*/|#.*?$)|(?<string>"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*')|(?<number>\b\d+(?:\.\d+)?\b)|(?<variable>\$[A-Za-z_][A-Za-z0-9_]*)|(?<keyword>\b(?:const|let|var|function|return|if|else|switch|case|break|continue|for|while|import|export|async|await|class|new|null|true|false|param|foreach|in|do)\b)""",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MarkupRegex = new(
        """(?<comment><!--[\s\S]*?-->)|(?<string>"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*')|(?<property>\b[A-Za-z_][A-Za-z0-9_:\-.]*(?=\s*=))|(?<keyword></?[A-Za-z_][A-Za-z0-9_:\-.]*)""",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public static IReadOnlyList<View> CreateViews(string? markdown, double baseFontSize)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return Array.Empty<View>();
        }

        MarkdownDocument document = Markdown.Parse(markdown.TrimEnd(), Pipeline);
        List<View> views = [];

        foreach (Block block in document)
        {
            View? view = CreateBlockView(block, baseFontSize, listDepth: 0);

            if (view != null)
            {
                views.Add(view);
            }
        }

        if (views.Count > 0)
        {
            views[^1].Margin = RemoveBottomMargin(views[^1].Margin);
        }

        return views;
    }

    private static View? CreateBlockView(Block block, double baseFontSize, int listDepth)
    {
        return block switch
        {
            HeadingBlock headingBlock => CreateTextLabel(
                FormatInlineContainer(
                    headingBlock.Inline,
                    CreateTextStyle(baseFontSize + Math.Max(1, 5 - headingBlock.Level), FontAttributes.Bold)),
                CreateBlockMargin()),
            ParagraphBlock paragraphBlock => CreateTextLabel(
                FormatInlineContainer(paragraphBlock.Inline, CreateTextStyle(baseFontSize, FontAttributes.None)),
                CreateBlockMargin()),
            QuoteBlock quoteBlock => CreateQuoteBlock(quoteBlock, baseFontSize, listDepth),
            ListBlock listBlock => CreateListBlock(listBlock, baseFontSize, listDepth),
            FencedCodeBlock fencedCodeBlock => CreateCodeBlock(
                fencedCodeBlock.Info?.ToString(),
                GetCodeBlockText(fencedCodeBlock),
                baseFontSize),
            CodeBlock codeBlock => CreateCodeBlock(null, GetCodeBlockText(codeBlock), baseFontSize),
            ThematicBreakBlock => new BoxView
            {
                HeightRequest = 1,
                Color = DividerColor,
                Margin = CreateBlockMargin()
            },
            Table table => CreateTable(table, baseFontSize),
            HtmlBlock htmlBlock => CreateTextLabel(
                CreatePlainFormattedString(WebUtility.HtmlDecode(htmlBlock.Lines.ToString()), baseFontSize),
                CreateBlockMargin()),
            _ => null
        };
    }

    private static View CreateQuoteBlock(QuoteBlock quoteBlock, double baseFontSize, int listDepth)
    {
        VerticalStackLayout contentStack = new()
        {
            Spacing = 0
        };

        foreach (Block childBlock in quoteBlock)
        {
            View? childView = CreateBlockView(childBlock, baseFontSize, listDepth);

            if (childView != null)
            {
                contentStack.Children.Add(childView);
            }
        }

        Grid quoteGrid = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = 3 },
                new ColumnDefinition { Width = GridLength.Star }
            },
            ColumnSpacing = 12,
            Margin = CreateBlockMargin()
        };

        quoteGrid.Children.Add(new BoxView
        {
            Color = QuoteBorderColor,
            VerticalOptions = LayoutOptions.Fill
        });
        quoteGrid.Children.Add(contentStack);
        Grid.SetColumn(contentStack, 1);

        return quoteGrid;
    }

    private static View CreateListBlock(ListBlock listBlock, double baseFontSize, int listDepth)
    {
        VerticalStackLayout itemsStack = new()
        {
            Spacing = 8,
            Margin = CreateBlockMargin()
        };

        int orderedIndex = int.TryParse(listBlock.OrderedStart, out int parsedOrderedStart)
            ? parsedOrderedStart
            : 1;
        int itemOffset = 0;

        foreach (Block item in listBlock)
        {
            if (item is not ListItemBlock listItemBlock)
            {
                continue;
            }

            string marker = listBlock.IsOrdered
                ? $"{orderedIndex + itemOffset}."
                : "\u2022";

            VerticalStackLayout itemContent = new()
            {
                Spacing = 6
            };

            foreach (Block childBlock in listItemBlock)
            {
                View? childView = CreateBlockView(childBlock, baseFontSize, listDepth + 1);

                if (childView != null)
                {
                    itemContent.Children.Add(childView);
                }
            }

            Grid itemGrid = new()
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Star }
                },
                ColumnSpacing = 10,
                Margin = new Thickness(listDepth * 12, 0, 0, 0)
            };

            itemGrid.Children.Add(new Label
            {
                Text = marker,
                FontSize = baseFontSize,
                FontAttributes = FontAttributes.Bold,
                TextColor = AccentSoftColor,
                VerticalTextAlignment = TextAlignment.Start
            });
            itemGrid.Children.Add(itemContent);
            Grid.SetColumn(itemContent, 1);

            itemsStack.Children.Add(itemGrid);
            itemOffset++;
        }

        return itemsStack;
    }

    private static View CreateCodeBlock(string? language, string code, double baseFontSize)
    {
        string normalizedCode = code.Replace("\r\n", "\n").TrimEnd('\n');
        string? normalizedLanguage = NormalizeLanguage(language);
        string displayLanguage = FormatLanguageLabel(normalizedLanguage);

        VerticalStackLayout stack = new()
        {
            Spacing = 0
        };

        if (!string.IsNullOrWhiteSpace(displayLanguage))
        {
            stack.Children.Add(new Border
            {
                BackgroundColor = CodeHeaderColor,
                StrokeThickness = 0,
                Padding = new Thickness(12, 8),
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14, 14, 0, 0) },
                Content = new Label
                {
                    Text = displayLanguage,
                    FontSize = Math.Max(11, baseFontSize - 4),
                    FontFamily = "OpenSansSemibold",
                    TextColor = AccentSoftColor
                }
            });
        }

        SelectableLabel codeLabel = new()
        {
            FormattedText = BuildCodeFormattedString(normalizedCode, normalizedLanguage, Math.Max(12, baseFontSize - 1)),
            LineBreakMode = LineBreakMode.NoWrap,
            Margin = 0,
            Padding = 0
        };

        stack.Children.Add(new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = codeLabel,
            Padding = new Thickness(14, 12)
        });

        return new Border
        {
            BackgroundColor = CodeSurfaceColor,
            Stroke = CodeBorderColor,
            StrokeThickness = 0.8,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            Margin = CreateBlockMargin(),
            Content = stack
        };
    }

    private static View CreateTable(Table table, double baseFontSize)
    {
        VerticalStackLayout rowsStack = new()
        {
            Spacing = 0
        };

        int rowIndex = 0;

        foreach (TableRow row in table)
        {
            FormattedTextBuilder builder = new();
            bool isFirstCell = true;

            foreach (TableCell cell in row)
            {
                if (!isFirstCell)
                {
                    builder.Append("   |   ", CreateTextStyle(baseFontSize - 1, FontAttributes.None) with
                    {
                        TextColor = AccentSoftColor
                    });
                }

                builder.Append(ExtractPlainText(cell).Trim(), CreateTextStyle(
                    baseFontSize - 1,
                    rowIndex == 0 ? FontAttributes.Bold : FontAttributes.None));
                isFirstCell = false;
            }

            rowsStack.Children.Add(new Border
            {
                BackgroundColor = rowIndex == 0 ? Color.FromArgb("#17122B") : Colors.Transparent,
                StrokeThickness = 0,
                Padding = new Thickness(12, 10),
                Content = CreateTextLabel(builder.Build(), 0)
            });

            if (rowIndex < table.Count - 1)
            {
                rowsStack.Children.Add(new BoxView
                {
                    HeightRequest = 1,
                    Color = DividerColor
                });
            }

            rowIndex++;
        }

        return new Border
        {
            BackgroundColor = Color.FromArgb("#0F0B18"),
            Stroke = CodeBorderColor,
            StrokeThickness = 0.8,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            Margin = CreateBlockMargin(),
            Content = rowsStack
        };
    }

    private static SelectableLabel CreateTextLabel(FormattedString formattedText, Thickness margin)
    {
        return new SelectableLabel
        {
            FormattedText = formattedText,
            TextColor = TextColor,
            LineBreakMode = LineBreakMode.WordWrap,
            Margin = margin,
            VerticalTextAlignment = TextAlignment.Start,
            HorizontalTextAlignment = TextAlignment.Start
        };
    }

    private static SelectableLabel CreateTextLabel(FormattedString formattedText, double top, double right = 0, double bottom = 0, double left = 0)
    {
        return CreateTextLabel(formattedText, new Thickness(left, top, right, bottom));
    }

    private static string GetCodeBlockText(LeafBlock leafBlock)
    {
        return string.Join("\n", leafBlock.Lines.Lines.Select(static line => line.ToString()));
    }

    private static Thickness CreateBlockMargin()
    {
        return new Thickness(0, 0, 0, 12);
    }

    private static Thickness RemoveBottomMargin(Thickness margin)
    {
        return new Thickness(margin.Left, margin.Top, margin.Right, 0);
    }

    private static FormattedString CreatePlainFormattedString(string text, double fontSize)
    {
        FormattedTextBuilder builder = new();
        builder.Append(text, CreateTextStyle(fontSize, FontAttributes.None));
        return builder.Build();
    }

    private static FormattedString FormatInlineContainer(ContainerInline? inlineContainer, MarkdownTextStyle textStyle)
    {
        FormattedTextBuilder builder = new();
        RenderInlineContainer(builder, inlineContainer, textStyle);
        return builder.Build();
    }

    private static void RenderInlineContainer(
        FormattedTextBuilder builder,
        ContainerInline? inlineContainer,
        MarkdownTextStyle textStyle)
    {
        if (inlineContainer == null)
        {
            return;
        }

        for (Inline? inline = inlineContainer.FirstChild; inline != null; inline = inline.NextSibling)
        {
            RenderInline(builder, inline, textStyle);
        }
    }

    private static void RenderInline(
        FormattedTextBuilder builder,
        Inline inline,
        MarkdownTextStyle textStyle)
    {
        switch (inline)
        {
            case LiteralInline literalInline:
                builder.Append(WebUtility.HtmlDecode(literalInline.Content.ToString()), textStyle);
                break;

            case LineBreakInline:
                builder.Append("\n", textStyle);
                break;

            case CodeInline codeInline:
                builder.Append(codeInline.Content, textStyle with
                {
                    FontFamily = GetCodeFontFamily(),
                    TextColor = InlineCodeTextColor,
                    BackgroundColor = InlineCodeBackgroundColor
                });
                break;

            case EmphasisInline emphasisInline:
                MarkdownTextStyle emphasisStyle = ApplyEmphasis(textStyle, emphasisInline);
                RenderInlineContainer(builder, emphasisInline, emphasisStyle);
                break;

            case LinkInline linkInline:
                MarkdownTextStyle linkStyle = textStyle with
                {
                    TextColor = AccentSoftColor,
                    TextDecorations = TextDecorations.Underline
                };

                if (linkInline.FirstChild == null && !string.IsNullOrWhiteSpace(linkInline.Url))
                {
                    builder.Append(linkInline.Url, linkStyle);
                }
                else
                {
                    RenderInlineContainer(builder, linkInline, linkStyle);
                }

                break;

            case ContainerInline containerInline:
                RenderInlineContainer(builder, containerInline, textStyle);
                break;
        }
    }

    private static MarkdownTextStyle ApplyEmphasis(MarkdownTextStyle currentStyle, EmphasisInline emphasisInline)
    {
        MarkdownTextStyle updatedStyle = currentStyle;

        if (emphasisInline.DelimiterCount >= 2)
        {
            updatedStyle = updatedStyle with
            {
                FontAttributes = updatedStyle.FontAttributes | FontAttributes.Bold
            };
        }
        else
        {
            updatedStyle = updatedStyle with
            {
                FontAttributes = updatedStyle.FontAttributes | FontAttributes.Italic
            };
        }

        return updatedStyle;
    }

    private static string ExtractPlainText(ContainerBlock containerBlock)
    {
        List<string> segments = [];

        foreach (Block block in containerBlock)
        {
            segments.Add(block switch
            {
                ParagraphBlock paragraphBlock => ExtractPlainText(paragraphBlock.Inline),
                LeafBlock leafBlock => GetCodeBlockText(leafBlock),
                _ => string.Empty
            });
        }

        return string.Join(" ", segments.Where(static segment => !string.IsNullOrWhiteSpace(segment)));
    }

    private static string ExtractPlainText(ContainerInline? inlineContainer)
    {
        if (inlineContainer == null)
        {
            return string.Empty;
        }

        List<string> segments = [];

        for (Inline? inline = inlineContainer.FirstChild; inline != null; inline = inline.NextSibling)
        {
            switch (inline)
            {
                case LiteralInline literalInline:
                    segments.Add(WebUtility.HtmlDecode(literalInline.Content.ToString()));
                    break;
                case CodeInline codeInline:
                    segments.Add(codeInline.Content);
                    break;
                case LineBreakInline:
                    segments.Add(" ");
                    break;
                case LinkInline linkInline when linkInline.FirstChild == null && !string.IsNullOrWhiteSpace(linkInline.Url):
                    segments.Add(linkInline.Url);
                    break;
                case ContainerInline childContainer:
                    segments.Add(ExtractPlainText(childContainer));
                    break;
            }
        }

        return string.Concat(segments);
    }

    private static FormattedString BuildCodeFormattedString(string code, string? language, double fontSize)
    {
        string normalizedCode = code.Replace("\r\n", "\n");
        Regex? tokenizer = GetCodeTokenizer(language);
        FormattedTextBuilder builder = new();

        if (tokenizer == null)
        {
            builder.Append(normalizedCode, CreateCodeStyle(fontSize, CodeDefaultColor));
            return builder.Build();
        }

        int currentIndex = 0;

        foreach (Match match in tokenizer.Matches(normalizedCode))
        {
            if (match.Index > currentIndex)
            {
                builder.Append(
                    normalizedCode[currentIndex..match.Index],
                    CreateCodeStyle(fontSize, CodeDefaultColor));
            }

            builder.Append(match.Value, ResolveCodeTokenStyle(match, fontSize));
            currentIndex = match.Index + match.Length;
        }

        if (currentIndex < normalizedCode.Length)
        {
            builder.Append(normalizedCode[currentIndex..], CreateCodeStyle(fontSize, CodeDefaultColor));
        }

        return builder.Build();
    }

    private static MarkdownTextStyle ResolveCodeTokenStyle(Match match, double fontSize)
    {
        if (match.Groups["comment"].Success)
        {
            return CreateCodeStyle(fontSize, CodeCommentColor);
        }

        if (match.Groups["string"].Success)
        {
            return CreateCodeStyle(fontSize, CodeStringColor);
        }

        if (match.Groups["number"].Success)
        {
            return CreateCodeStyle(fontSize, CodeNumberColor);
        }

        if (match.Groups["type"].Success)
        {
            return CreateCodeStyle(fontSize, CodeTypeColor);
        }

        if (match.Groups["property"].Success)
        {
            return CreateCodeStyle(fontSize, CodePropertyColor);
        }

        if (match.Groups["variable"].Success)
        {
            return CreateCodeStyle(fontSize, CodeVariableColor);
        }

        if (match.Groups["keyword"].Success)
        {
            return CreateCodeStyle(fontSize, CodeKeywordColor);
        }

        return CreateCodeStyle(fontSize, CodeDefaultColor);
    }

    private static Regex? GetCodeTokenizer(string? language)
    {
        return language switch
        {
            "csharp" => CSharpRegex,
            "json" => JsonRegex,
            "sql" => SqlRegex,
            "javascript" or "typescript" or "powershell" or "bash" or "shell" => ScriptRegex,
            "xml" or "html" or "xaml" => MarkupRegex,
            _ => null
        };
    }

    private static string? NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        string normalizedLanguage = language.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();

        return normalizedLanguage switch
        {
            "cs" or "c#" => "csharp",
            "js" => "javascript",
            "ts" => "typescript",
            "ps" or "ps1" or "pwsh" => "powershell",
            "sh" => "shell",
            "htm" => "html",
            _ => normalizedLanguage
        };
    }

    private static string FormatLanguageLabel(string? language)
    {
        return language switch
        {
            null => string.Empty,
            "csharp" => "C#",
            "javascript" => "JavaScript",
            "typescript" => "TypeScript",
            "powershell" => "PowerShell",
            "shell" => "Shell",
            "json" => "JSON",
            "sql" => "SQL",
            "xml" => "XML",
            "html" => "HTML",
            "xaml" => "XAML",
            _ => language.ToUpperInvariant()
        };
    }

    private static MarkdownTextStyle CreateTextStyle(double fontSize, FontAttributes fontAttributes)
    {
        return new MarkdownTextStyle(
            fontSize,
            fontAttributes,
            TextColor,
            "OpenSansRegular",
            TextDecorations.None,
            null);
    }

    private static MarkdownTextStyle CreateCodeStyle(double fontSize, Color color)
    {
        return new MarkdownTextStyle(
            fontSize,
            FontAttributes.None,
            color,
            GetCodeFontFamily(),
            TextDecorations.None,
            null);
    }

    private static string GetCodeFontFamily()
    {
#if ANDROID
        return "monospace";
#elif WINDOWS
        return "Consolas";
#else
        return "Courier New";
#endif
    }

    private sealed class FormattedTextBuilder
    {
        private readonly FormattedString formattedString = new();

        public void Append(string text, MarkdownTextStyle textStyle)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            Span span = new()
            {
                Text = text,
                FontSize = textStyle.FontSize,
                FontAttributes = textStyle.FontAttributes,
                TextColor = textStyle.TextColor,
                FontFamily = textStyle.FontFamily,
                TextDecorations = textStyle.TextDecorations
            };

            if (textStyle.BackgroundColor != null)
            {
                span.BackgroundColor = textStyle.BackgroundColor;
            }

            formattedString.Spans.Add(span);
        }

        public FormattedString Build()
        {
            return formattedString;
        }
    }

    private readonly record struct MarkdownTextStyle(
        double FontSize,
        FontAttributes FontAttributes,
        Color TextColor,
        string FontFamily,
        TextDecorations TextDecorations,
        Color? BackgroundColor);
}
