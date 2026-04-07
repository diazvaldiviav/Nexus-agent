using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexus.Memory.Abstractions;
using Nexus.Memory.Models;
using System.Collections.ObjectModel;

namespace Nexus.Desktop.ViewModels;

public partial class ActionLogViewModel : ObservableObject, IDisposable
{
    private readonly IKnowledgeGraph _graph;
    private readonly IActionLogNotifier _notifier;
    private bool _disposed;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _filterType = "All";

    public ObservableCollection<AgentAction> Actions { get; } = new();
    public ObservableCollection<string> ActionTypes { get; } = new(
        new[] { "All", "chat", "entity_extraction", "summarize", "decay", "mcp" });

    public bool HasActions => Actions.Count > 0;

    public ActionLogViewModel(IKnowledgeGraph graph, IActionLogNotifier notifier)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
        _notifier.ActionLogged += OnActionLogged;
        Actions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasActions));
    }

    [RelayCommand]
    public async Task LoadActionsAsync()
    {
        IsLoading = true;
        try
        {
            var actions = await _graph.GetRecentActionsAsync(200);
            Actions.Clear();

            var filtered = FilterType == "All"
                ? actions
                : actions.Where(a => a.ActionType == FilterType).ToList();

            foreach (var action in filtered)
                Actions.Add(action);
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnFilterTypeChanged(string value) => _ = LoadActionsAsync();

    private void OnActionLogged(AgentAction action)
    {
        if (_disposed) return;
        if (IsLoading) return;
        if (FilterType != "All" && action.ActionType != FilterType) return;
        DispatchToUI(() => Actions.Insert(0, action));
    }

    protected virtual void DispatchToUI(Action action)
        => Avalonia.Threading.Dispatcher.UIThread.Post(action);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notifier.ActionLogged -= OnActionLogged;
        GC.SuppressFinalize(this);
    }
}
