using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Nexus.Desktop.Controls;

namespace Nexus.Desktop.Tests;

public class MarkdownTextBlockTests
{
    [AvaloniaFact]
    public void Render_WithMarkdown_ProducesControls()
    {
        // Arrange & Act — test the core rendering logic directly
        var controls = MarkdownRenderer.Render("# Hello\n\nSome **bold** text.");

        // Assert
        Assert.NotEmpty(controls);
        Assert.True(controls.Count >= 2); // heading + paragraph
    }

    [AvaloniaFact]
    public void Render_SameTextTwice_ReturnsSameResult()
    {
        // Arrange
        var markdown = "Hello world";

        // Act — rendering the same text twice should produce equivalent output
        var first = MarkdownRenderer.Render(markdown);
        var second = MarkdownRenderer.Render(markdown);

        // Assert — both produce the same number of controls (render guard would skip in the control)
        Assert.Equal(first.Count, second.Count);
    }

    [AvaloniaFact]
    public void Render_NullText_ReturnsEmpty()
    {
        // Arrange & Act — null input produces no controls (detached guard equivalent)
        var controls = MarkdownRenderer.Render(null);

        // Assert
        Assert.Empty(controls);
    }

    [AvaloniaFact]
    public void Text_SetProperty_StoresValue()
    {
        // Arrange
        var control = new MarkdownTextBlock();

        // Act
        control.Text = "# Heading";

        // Assert — Text property is stored even before timer fires
        Assert.Equal("# Heading", control.Text);
    }

    [AvaloniaFact]
    public void Text_InitiallyNull_ContentPanelEmpty()
    {
        // Arrange & Act
        var control = new MarkdownTextBlock();

        // Assert — content panel exists but has no rendered children
        var panel = control.Content as StackPanel;
        Assert.NotNull(panel);
        Assert.Empty(panel.Children);
    }

    [AvaloniaFact]
    public void Text_SetNull_ContentRemainsEmpty()
    {
        // Arrange
        var control = new MarkdownTextBlock();

        // Act
        control.Text = null;

        // Assert — null text should not produce rendered content
        var panel = control.Content as StackPanel;
        Assert.NotNull(panel);
        Assert.Empty(panel.Children);
    }

    [AvaloniaFact]
    public void Lifecycle_AttachAndTick_RendersContent()
    {
        // Arrange — simulate lifecycle by invoking the tick handler directly
        var control = new MarkdownTextBlock();
        var window = new Window { Content = control };
        window.Show();

        control.Text = "# Hello World";

        // Act — invoke the debounce tick handler via reflection
        var tickMethod = typeof(MarkdownTextBlock).GetMethod(
            "OnDebounceTimerTick", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(tickMethod);
        tickMethod.Invoke(control, [null, EventArgs.Empty]);

        // Assert — content panel should have rendered children
        var panel = control.Content as StackPanel;
        Assert.NotNull(panel);
        Assert.NotEmpty(panel.Children);

        window.Close();
    }

    [AvaloniaFact]
    public void Lifecycle_SameTextTwice_RendersOnlyOnce()
    {
        // Arrange — attach and render once
        var control = new MarkdownTextBlock();
        var window = new Window { Content = control };
        window.Show();

        control.Text = "Same text";

        var tickMethod = typeof(MarkdownTextBlock).GetMethod(
            "OnDebounceTimerTick", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(tickMethod);
        tickMethod.Invoke(control, [null, EventArgs.Empty]);

        var panel = control.Content as StackPanel;
        Assert.NotNull(panel);
        var firstChildCount = panel.Children.Count;
        Assert.True(firstChildCount > 0);

        // Act — set same text again, tick again; _lastRenderedText guard should skip
        control.Text = "Same text";
        tickMethod.Invoke(control, [null, EventArgs.Empty]);

        // Assert — children count unchanged (no redundant re-render)
        Assert.Equal(firstChildCount, panel.Children.Count);

        window.Close();
    }

    [AvaloniaFact]
    public void Lifecycle_DetachedGuard_PreventsRender()
    {
        // Arrange — attach, then detach
        var control = new MarkdownTextBlock();
        var window = new Window { Content = control };
        window.Show();

        // Detach by removing from visual tree
        window.Content = null;

        // Act — set text and invoke tick after detach
        control.Text = "# Should not render";
        var tickMethod = typeof(MarkdownTextBlock).GetMethod(
            "OnDebounceTimerTick", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(tickMethod);
        tickMethod.Invoke(control, [null, EventArgs.Empty]);

        // Assert — content panel should remain empty because _isDetached is true
        var panel = control.Content as StackPanel;
        Assert.NotNull(panel);
        Assert.Empty(panel.Children);

        window.Close();
    }
}
