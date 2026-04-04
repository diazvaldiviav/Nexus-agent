using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Nexus.Desktop.Controls;

namespace Nexus.Desktop.Tests;

public class MarkdownRendererTests
{
    [AvaloniaFact]
    public void Render_BoldText_ReturnsBoldRun()
    {
        var controls = MarkdownRenderer.Render("**bold**");

        var tb = Assert.Single(controls);
        var textBlock = Assert.IsType<TextBlock>(tb);
        var run = Assert.Single(textBlock.Inlines!.OfType<Run>());
        Assert.Equal("bold", run.Text);
        Assert.Equal(FontWeight.Bold, run.FontWeight);
    }

    [AvaloniaFact]
    public void Render_ItalicText_ReturnsItalicRun()
    {
        var controls = MarkdownRenderer.Render("*italic*");

        var tb = Assert.Single(controls);
        var textBlock = Assert.IsType<TextBlock>(tb);
        var run = Assert.Single(textBlock.Inlines!.OfType<Run>());
        Assert.Equal("italic", run.Text);
        Assert.Equal(FontStyle.Italic, run.FontStyle);
    }

    [AvaloniaFact]
    public void Render_H1_ReturnsFontSize24()
    {
        var controls = MarkdownRenderer.Render("# Title");

        var tb = Assert.Single(controls);
        var textBlock = Assert.IsType<TextBlock>(tb);
        Assert.Equal(24.0, textBlock.FontSize);
    }

    [AvaloniaFact]
    public void Render_H2_ReturnsFontSize20()
    {
        var controls = MarkdownRenderer.Render("## Sub");

        var tb = Assert.Single(controls);
        var textBlock = Assert.IsType<TextBlock>(tb);
        Assert.Equal(20.0, textBlock.FontSize);
    }

    [AvaloniaFact]
    public void Render_H3_ReturnsFontSize16()
    {
        var controls = MarkdownRenderer.Render("### Sub2");

        var tb = Assert.Single(controls);
        var textBlock = Assert.IsType<TextBlock>(tb);
        Assert.Equal(16.0, textBlock.FontSize);
    }

    [AvaloniaFact]
    public void Render_InlineCode_ReturnsMonospaceRun()
    {
        var controls = MarkdownRenderer.Render("`code`");

        var tb = Assert.Single(controls);
        var textBlock = Assert.IsType<TextBlock>(tb);
        var run = Assert.Single(textBlock.Inlines!.OfType<Run>());
        Assert.Equal("code", run.Text);
        Assert.NotNull(run.FontFamily);
        Assert.Contains("monospace", run.FontFamily.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void Render_FencedCodeBlock_ReturnsBorderWithBackground()
    {
        var controls = MarkdownRenderer.Render("```\ncode here\n```");

        var border = Assert.Single(controls);
        var b = Assert.IsType<Border>(border);

        // Verify background color is #1e1e2e (Base)
        Assert.NotNull(b.Background);
        var brush = b.Background as ISolidColorBrush;
        Assert.NotNull(brush);
        Assert.Equal(Color.Parse("#1e1e2e"), brush.Color);

        // Verify child is ScrollViewer wrapping SelectableTextBlock
        var sv = Assert.IsType<ScrollViewer>(b.Child);
        var stb = Assert.IsType<SelectableTextBlock>(sv.Content);
        Assert.Contains("code here", stb.Text);
    }

    [AvaloniaFact]
    public void Render_BulletList_ReturnsIndentedItems()
    {
        var controls = MarkdownRenderer.Render("- item1\n- item2");

        var panel = Assert.Single(controls);
        var sp = Assert.IsType<StackPanel>(panel);
        Assert.True(sp.Margin.Left > 0, "List should have left margin for indentation");
        Assert.Equal(2, sp.Children.Count);
    }

    [AvaloniaFact]
    public void Render_NumberedList_ReturnsNumberedItems()
    {
        var controls = MarkdownRenderer.Render("1. first\n2. second");

        var panel = Assert.Single(controls);
        var sp = Assert.IsType<StackPanel>(panel);
        Assert.Equal(2, sp.Children.Count);

        // First item should have "1. " prefix
        var firstItem = Assert.IsType<StackPanel>(sp.Children[0]);
        var prefix = Assert.IsType<TextBlock>(firstItem.Children[0]);
        Assert.Equal("1. ", prefix.Text);
    }

    [AvaloniaFact]
    public void Render_Link_ReturnsStyledElement()
    {
        var controls = MarkdownRenderer.Render("[click](https://example.com)");

        var control = Assert.Single(controls);
        // Standalone link renders as a clickable TextBlock
        var tb = Assert.IsType<TextBlock>(control);
        Assert.Equal("click", tb.Text);

        var brush = tb.Foreground as ISolidColorBrush;
        Assert.NotNull(brush);
        Assert.Equal(Color.Parse("#89b4fa"), brush.Color);
        Assert.NotNull(tb.TextDecorations);
    }

    [AvaloniaFact]
    public void Render_PlainText_ReturnsSingleTextBlock()
    {
        var controls = MarkdownRenderer.Render("Hello world");

        var tb = Assert.Single(controls);
        var textBlock = Assert.IsType<TextBlock>(tb);
        var run = Assert.Single(textBlock.Inlines!.OfType<Run>());
        Assert.Equal("Hello world", run.Text);
    }

    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData("")]
    public void Render_NullOrEmpty_ReturnsEmptyList(string? input)
    {
        var controls = MarkdownRenderer.Render(input);

        Assert.Empty(controls);
    }

    /// <summary>
    /// Tests visual-only rendering of inline links within mixed paragraphs.
    /// Inline Runs are styled with link color but are not clickable
    /// (Avalonia 11 limitation — no InlineUIContainer for click handling).
    /// </summary>
    [AvaloniaFact]
    public void Render_InlineLink_WithSurroundingText_ReturnsRunWithLinkColor()
    {
        var controls = MarkdownRenderer.Render("See [docs](https://example.com) for details");

        var tb = Assert.Single(controls);
        var textBlock = Assert.IsType<TextBlock>(tb);

        var runs = textBlock.Inlines!.OfType<Run>().ToList();
        Assert.True(runs.Count >= 3, "Expected at least 3 runs: 'See ', 'docs', ' for details'");

        var linkRun = runs.First(r => r.Text == "docs");
        var brush = linkRun.Foreground as ISolidColorBrush;
        Assert.NotNull(brush);
        Assert.Equal(Color.Parse("#89b4fa"), brush.Color);
    }
}
