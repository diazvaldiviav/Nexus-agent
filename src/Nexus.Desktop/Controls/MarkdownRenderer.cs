using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using AvaloniaInline = Avalonia.Controls.Documents.Inline;

namespace Nexus.Desktop.Controls;

/// <summary>
/// Converts markdown text into a list of Avalonia controls for rendering.
/// Static helper — never throws; returns a plain TextBlock on failure.
/// </summary>
public static class MarkdownRenderer
{
    private static readonly ImmutableSolidColorBrush s_textBrush = new(Color.Parse("#cdd6f4"));
    private static readonly ImmutableSolidColorBrush s_linkBrush = new(Color.Parse("#89b4fa"));
    private static readonly ImmutableSolidColorBrush s_codeBlockBg = new(Color.Parse("#1e1e2e"));
    private static readonly ImmutableSolidColorBrush s_codeBlockBorder = new(Color.Parse("#313244"));
    private static readonly ImmutableSolidColorBrush s_inlineCodeBg = new(Color.Parse("#313244"));

    private static readonly FontFamily s_monoFont = new("Cascadia Code,Consolas,Courier New,monospace");

    private static readonly MarkdownPipeline s_pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .Build();

    /// <summary>
    /// Renders markdown text into a list of Avalonia controls.
    /// Returns an empty list for null/empty input. Never throws.
    /// </summary>
    public static IReadOnlyList<Control> Render(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return Array.Empty<Control>();

        try
        {
            var document = Markdown.Parse(markdown, s_pipeline);
            var controls = new List<Control>();

            foreach (var block in document)
            {
                var control = RenderBlock(block);
                if (control != null)
                    controls.Add(control);
            }

            return controls;
        }
        catch
        {
            return new Control[]
            {
                new TextBlock
                {
                    Text = markdown,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = s_textBrush,
                }
            };
        }
    }

    private static Control? RenderBlock(Block block)
    {
        return block switch
        {
            HeadingBlock heading => RenderHeading(heading),
            ParagraphBlock paragraph => RenderParagraph(paragraph),
            FencedCodeBlock fencedCode => RenderFencedCode(fencedCode),
            CodeBlock codeBlock => RenderFencedCode(codeBlock),
            ListBlock list => RenderList(list),
            ThematicBreakBlock => new Border
            {
                Height = 1,
                Background = s_codeBlockBorder,
                Margin = new Thickness(0, 8),
            },
            _ => null,
        };
    }

    private static Control RenderHeading(HeadingBlock heading)
    {
        var fontSize = heading.Level switch
        {
            1 => 24.0,
            2 => 20.0,
            3 => 16.0,
            _ => 14.0,
        };

        var tb = new TextBlock
        {
            FontSize = fontSize,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = s_textBrush,
            Margin = new Thickness(0, 4, 0, 2),
        };

        if (heading.Inline != null)
        {
            foreach (var run in RenderInlines(heading.Inline))
                tb.Inlines!.Add(run);
        }

        return tb;
    }

    private static Control RenderParagraph(ParagraphBlock paragraph)
    {
        if (paragraph.Inline == null)
            return new TextBlock { Text = "", Foreground = s_textBrush };

        // Check if the paragraph is a single standalone link
        var inlineCount = 0;
        LinkInline? singleLink = null;
        foreach (var inline in paragraph.Inline)
        {
            inlineCount++;
            if (inline is LinkInline link && inlineCount == 1)
                singleLink = link;
        }

        if (inlineCount == 1 && singleLink != null && !string.IsNullOrEmpty(singleLink.Url))
        {
            return RenderClickableLink(singleLink);
        }

        var tb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = s_textBrush,
            Margin = new Thickness(0, 2),
        };

        foreach (var run in RenderInlines(paragraph.Inline))
            tb.Inlines!.Add(run);

        return tb;
    }

    private static Control RenderClickableLink(LinkInline link)
    {
        var linkText = GetLinkText(link);
        var url = link.Url ?? "";

        var tb = new TextBlock
        {
            Text = linkText,
            Foreground = s_linkBrush,
            TextDecorations = TextDecorations.Underline,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Margin = new Thickness(0, 2),
        };

        tb.PointerPressed += (_, _) => OpenUrl(url);
        return tb;
    }

    private static string GetLinkText(LinkInline link)
    {
        var sb = new System.Text.StringBuilder();
        if (link.FirstChild != null)
        {
            foreach (var child in link)
            {
                if (child is LiteralInline literal)
                    sb.Append(literal.Content);
            }
        }

        var text = sb.ToString();
        return string.IsNullOrEmpty(text) ? link.Url ?? "link" : text;
    }

    private static Control RenderFencedCode(CodeBlock codeBlock)
    {
        var code = codeBlock.Lines.ToString().TrimEnd();

        var stb = new SelectableTextBlock
        {
            Text = code,
            FontFamily = s_monoFont,
            FontSize = 13,
            Foreground = s_textBrush,
            TextWrapping = TextWrapping.NoWrap,
        };

        var scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = stb,
        };

        return new Border
        {
            Background = s_codeBlockBg,
            BorderBrush = s_codeBlockBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 8),
            Margin = new Thickness(0, 4),
            Child = scrollViewer,
        };
    }

    private static Control RenderList(ListBlock list)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(16, 4, 0, 4),
            Spacing = 2,
        };

        var index = 1;
        foreach (var item in list)
        {
            if (item is not ListItemBlock listItem) continue;

            var prefix = list.IsOrdered ? $"{index}. " : "- ";
            index++;

            var itemPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
            };

            itemPanel.Children.Add(new TextBlock
            {
                Text = prefix,
                Foreground = s_textBrush,
                VerticalAlignment = VerticalAlignment.Top,
            });

            var contentPanel = new StackPanel();
            foreach (var subBlock in listItem)
            {
                if (subBlock is ParagraphBlock para && para.Inline != null)
                {
                    var tb = new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = s_textBrush,
                    };

                    foreach (var run in RenderInlines(para.Inline))
                        tb.Inlines!.Add(run);

                    contentPanel.Children.Add(tb);
                }
            }

            itemPanel.Children.Add(contentPanel);
            panel.Children.Add(itemPanel);
        }

        return panel;
    }

    private static IEnumerable<AvaloniaInline> RenderInlines(ContainerInline container)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    yield return new Run
                    {
                        Text = literal.Content.ToString(),
                        Foreground = s_textBrush,
                    };
                    break;

                case EmphasisInline emphasis:
                    foreach (var child in RenderEmphasis(emphasis))
                        yield return child;
                    break;

                case CodeInline code:
                    yield return new Run
                    {
                        Text = code.Content,
                        FontFamily = s_monoFont,
                        Background = s_inlineCodeBg,
                        Foreground = s_textBrush,
                    };
                    break;

                case LinkInline link:
                    foreach (var child in RenderLinkInline(link))
                        yield return child;
                    break;

                case LineBreakInline:
                    yield return new LineBreak();
                    break;
            }
        }
    }

    private static IEnumerable<AvaloniaInline> RenderEmphasis(EmphasisInline emphasis)
    {
        var isBold = emphasis.DelimiterChar is '*' or '_' && emphasis.DelimiterCount >= 2;
        var isItalic = emphasis.DelimiterChar is '*' or '_' && emphasis.DelimiterCount == 1;

        foreach (var child in emphasis)
        {
            if (child is LiteralInline literal)
            {
                var run = new Run
                {
                    Text = literal.Content.ToString(),
                    Foreground = s_textBrush,
                };

                if (isBold) run.FontWeight = FontWeight.Bold;
                if (isItalic) run.FontStyle = FontStyle.Italic;

                yield return run;
            }
            else if (child is EmphasisInline nestedEmphasis)
            {
                foreach (var nested in RenderEmphasis(nestedEmphasis))
                    yield return nested;
            }
            else if (child is CodeInline code)
            {
                yield return new Run
                {
                    Text = code.Content,
                    FontFamily = s_monoFont,
                    Background = s_inlineCodeBg,
                    Foreground = s_textBrush,
                    FontWeight = isBold ? FontWeight.Bold : FontWeight.Normal,
                    FontStyle = isItalic ? FontStyle.Italic : FontStyle.Normal,
                };
            }
        }
    }

    /// <summary>
    /// Renders an inline link as a styled Run with link color and underline.
    /// Note: Inline links within mixed paragraphs are visual-only (not clickable).
    /// Avalonia 11 does not support InlineUIContainer, so click handling is only
    /// possible for standalone paragraph-level links via RenderClickableLink.
    /// </summary>
    private static IEnumerable<AvaloniaInline> RenderLinkInline(LinkInline link)
    {
        var text = GetLinkText(link);
        yield return new Run
        {
            Text = text,
            Foreground = s_linkBrush,
            TextDecorations = TextDecorations.Underline,
        };
    }

    internal static void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return;

        if (uri.Scheme is not ("http" or "https"))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Silently ignore if browser cannot be opened
        }
    }
}
