using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexus.Memory;
using Nexus.Memory.Models;
using System.Collections.ObjectModel;

namespace Nexus.Desktop.ViewModels;

public partial class ActionLogViewModel : ObservableObject
{
    private readonly KnowledgeGraph _graph;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _filterType = "All";

    public ObservableCollection<AgentAction> Actions { get; } = new();
    public ObservableCollection<string> ActionTypes { get; } = new(
        new[] { "All", "chat", "entity_extraction", "summarize", "decay" });

    public ActionLogViewModel(KnowledgeGraph graph)
    {
        _graph = graph;
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
}
