using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Nexus.Desktop.ViewModels;

namespace Nexus.Desktop.Views;

public partial class ChatView : UserControl
{
    private ScrollViewer? _scroller;
    private bool _autoScrollEnabled = true;
    private bool _isProgrammaticScroll;
    private ChatMessage? _trackedMessage;

    public ChatView()
    {
        InitializeComponent();
    }

    // Note: DataContext is set once during construction and never changes at runtime.
    // ChatView is created fresh for each navigation, so there is no DataContext-change leak.
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _scroller = this.FindControl<ScrollViewer>("MessagesScroller");
        if (_scroller is not null)
            _scroller.ScrollChanged += OnScrollChanged;
        if (DataContext is ChatViewModel vm)
            vm.Messages.CollectionChanged += OnMessagesCollectionChanged;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        if (_scroller is not null)
            _scroller.ScrollChanged -= OnScrollChanged;
        if (DataContext is ChatViewModel vm)
            vm.Messages.CollectionChanged -= OnMessagesCollectionChanged;
        UntrackLastMessage();
        base.OnUnloaded(e);
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_scroller is null || _isProgrammaticScroll) return;
        const double threshold = 50;
        _autoScrollEnabled = _scroller.Offset.Y + _scroller.Viewport.Height
                             >= _scroller.Extent.Height - threshold;
    }

    private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_autoScrollEnabled) return;
        UntrackLastMessage();
        if (DataContext is ChatViewModel vm && vm.Messages.Count > 0)
        {
            _trackedMessage = vm.Messages[^1];
            _trackedMessage.PropertyChanged += OnLastMessagePropertyChanged;
        }
        ScrollToBottom();
    }

    private void OnLastMessagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatMessage.Content) && _autoScrollEnabled)
            ScrollToBottom();
    }

    private void UntrackLastMessage()
    {
        if (_trackedMessage is not null)
        {
            _trackedMessage.PropertyChanged -= OnLastMessagePropertyChanged;
            _trackedMessage = null;
        }
    }

    private void ScrollToBottom()
    {
        _isProgrammaticScroll = true;
        Dispatcher.UIThread.Post(() =>
        {
            _scroller?.ScrollToEnd();
            _isProgrammaticScroll = false;
        }, DispatcherPriority.Background);
    }
}
