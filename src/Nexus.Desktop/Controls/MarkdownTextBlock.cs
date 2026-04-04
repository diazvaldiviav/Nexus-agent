using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Nexus.Desktop.Controls;

/// <summary>
/// A UserControl that renders markdown text into styled Avalonia controls.
/// Uses debounced rendering (250ms) to handle streaming token updates efficiently.
/// </summary>
public class MarkdownTextBlock : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<MarkdownTextBlock, string?>(nameof(Text));

    private readonly StackPanel _contentPanel;
    private readonly DispatcherTimer _debounceTimer;
    private string? _lastRenderedText;
    private bool _isDetached;

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public MarkdownTextBlock()
    {
        _contentPanel = new StackPanel { Spacing = 4 };
        Content = _contentPanel;

        _debounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
    }

    static MarkdownTextBlock()
    {
        TextProperty.Changed.AddClassHandler<MarkdownTextBlock>(OnTextChanged);
    }

    private static void OnTextChanged(MarkdownTextBlock sender, AvaloniaPropertyChangedEventArgs e)
    {
        sender._debounceTimer.Stop();
        sender._debounceTimer.Start();
    }

    private void OnDebounceTimerTick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();

        if (_isDetached)
            return;

        var currentText = Text;
        if (currentText == _lastRenderedText)
            return;

        _lastRenderedText = currentText;

        var controls = MarkdownRenderer.Render(currentText);
        _contentPanel.Children.Clear();
        foreach (var control in controls)
            _contentPanel.Children.Add(control);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isDetached = false;
        _debounceTimer.Tick += OnDebounceTimerTick;
        _lastRenderedText = null;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isDetached = true;
        _debounceTimer.Stop();
        _debounceTimer.Tick -= OnDebounceTimerTick;
        base.OnDetachedFromVisualTree(e);
    }
}
