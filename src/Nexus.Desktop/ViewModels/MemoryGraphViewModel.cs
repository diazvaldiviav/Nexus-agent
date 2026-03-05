using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexus.Memory;
using Nexus.Memory.Models;
using System.Collections.ObjectModel;

namespace Nexus.Desktop.ViewModels;

public class GraphNode
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public EntityType Type { get; set; }
    public double RelevanceScore { get; set; }
    public string? Summary { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Size => Math.Max(20, Math.Min(60, RelevanceScore * 50));
}

public class GraphEdge
{
    public string SourceId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string RelationType { get; set; } = string.Empty;
}

public partial class MemoryGraphViewModel : ObservableObject
{
    private readonly KnowledgeGraph _graph;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _selectedEntityType = "All";

    [ObservableProperty]
    private GraphNode? _selectedNode;

    [ObservableProperty]
    private string _selectedNodeDetails = string.Empty;

    public ObservableCollection<GraphNode> Nodes { get; } = new();
    public ObservableCollection<GraphEdge> Edges { get; } = new();
    public ObservableCollection<string> EntityTypes { get; } = new(
        new[] { "All", "Person", "Project", "Technology", "Decision", "Date", "Preference", "Other" });

    public MemoryGraphViewModel(KnowledgeGraph graph)
    {
        _graph = graph;
    }

    [RelayCommand]
    public async Task LoadGraphAsync()
    {
        IsLoading = true;
        try
        {
            var entities = await _graph.GetAllEntitiesAsync();
            var relations = await _graph.GetAllRelationsAsync();

            Nodes.Clear();
            Edges.Clear();

            var filtered = SelectedEntityType == "All"
                ? entities
                : entities.Where(e => e.Type.ToString() == SelectedEntityType).ToList();

            var rng = new Random(42);
            var centerX = 400.0;
            var centerY = 300.0;
            var radius = 250.0;

            for (int i = 0; i < filtered.Count; i++)
            {
                var e = filtered[i];
                var angle = 2 * Math.PI * i / Math.Max(1, filtered.Count);
                Nodes.Add(new GraphNode
                {
                    Id = e.Id,
                    Name = e.Name,
                    Type = e.Type,
                    RelevanceScore = e.RelevanceScore,
                    Summary = e.TextSummary,
                    X = centerX + radius * Math.Cos(angle) * (0.5 + rng.NextDouble() * 0.5),
                    Y = centerY + radius * Math.Sin(angle) * (0.5 + rng.NextDouble() * 0.5)
                });
            }

            var nodeIds = Nodes.Select(n => n.Id).ToHashSet();
            foreach (var r in relations.Where(r => nodeIds.Contains(r.EntityId1) && nodeIds.Contains(r.EntityId2)))
            {
                Edges.Add(new GraphEdge
                {
                    SourceId = r.EntityId1,
                    TargetId = r.EntityId2,
                    RelationType = r.RelationType
                });
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void SelectNode(GraphNode? node)
    {
        SelectedNode = node;
        SelectedNodeDetails = node != null
            ? $"Name: {node.Name}\nType: {node.Type}\nScore: {node.RelevanceScore:F2}\n{(node.Summary ?? "No summary")}"
            : string.Empty;
    }

    partial void OnSelectedEntityTypeChanged(string value) => _ = LoadGraphAsync();
}
