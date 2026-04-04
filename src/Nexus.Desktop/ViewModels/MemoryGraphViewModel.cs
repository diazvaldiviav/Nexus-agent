using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexus.Desktop.Layout;
using Nexus.Memory.Abstractions;
using Nexus.Memory.Models;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

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
    public bool IsPinned { get; set; }
    public double Size => Math.Max(20, Math.Min(60, RelevanceScore * 50));
}

public class GraphEdge
{
    public string SourceId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string RelationType { get; set; } = string.Empty;
}

public partial class EntityTypeFilter : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected = true;

    public string TypeName { get; init; } = string.Empty;
}

public partial class MemoryGraphViewModel : ObservableObject
{
    private readonly IKnowledgeGraph _graph;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private GraphNode? _selectedNode;

    [ObservableProperty]
    private string _selectedNodeDetails = string.Empty;

    [ObservableProperty]
    private bool _isSimulating;

    public event EventHandler? LayoutUpdated;

    public ObservableCollection<GraphNode> Nodes { get; } = new();
    public ObservableCollection<GraphEdge> Edges { get; } = new();
    public ObservableCollection<EntityTypeFilter> EntityTypeFilters { get; } = new();

    public bool HasNodes => Nodes.Count > 0;

    /// <summary>All loaded nodes before type filtering is applied.</summary>
    private List<GraphNode> _allNodes = new();
    private List<GraphEdge> _allEdges = new();

    private ForceDirectedLayout? _layout;
    private List<LayoutNode>? _layoutNodes;
    private List<LayoutEdge>? _layoutEdges;
    private DispatcherTimer? _layoutTimer;
    private double _temperature;
    private int _iteration;

    private const double CanvasWidth = 800.0;
    private const double CanvasHeight = 600.0;

    public MemoryGraphViewModel(IKnowledgeGraph graph)
    {
        _graph = graph;
        Nodes.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNodes));
    }

    [RelayCommand]
    public async Task LoadGraphAsync()
    {
        IsLoading = true;
        StopSimulation();
        try
        {
            var entities = await _graph.GetAllEntitiesAsync();
            var relations = await _graph.GetAllRelationsAsync();

            _allNodes.Clear();
            _allEdges.Clear();

            foreach (var e in entities)
            {
                _allNodes.Add(new GraphNode
                {
                    Id = e.Id,
                    Name = e.Name,
                    Type = e.Type,
                    RelevanceScore = e.RelevanceScore,
                    Summary = e.TextSummary
                });
            }

            var nodeIds = _allNodes.Select(n => n.Id).ToHashSet();
            foreach (var r in relations.Where(r => nodeIds.Contains(r.EntityId1) && nodeIds.Contains(r.EntityId2)))
            {
                _allEdges.Add(new GraphEdge
                {
                    SourceId = r.EntityId1,
                    TargetId = r.EntityId2,
                    RelationType = r.RelationType
                });
            }

            PopulateEntityTypeFilters();
            ApplyFilterAndLayout();
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

    [RelayCommand]
    public void SelectAllFilters()
    {
        foreach (var filter in EntityTypeFilters)
            filter.IsSelected = true;

        ApplyFilterAndLayout();
    }

    [RelayCommand]
    public void ClearAllFilters()
    {
        foreach (var filter in EntityTypeFilters)
            filter.IsSelected = false;

        ApplyFilterAndLayout();
    }

    private void PopulateEntityTypeFilters()
    {
        var existingTypes = EntityTypeFilters.ToDictionary(f => f.TypeName, f => f.IsSelected);

        EntityTypeFilters.Clear();
        var distinctTypes = _allNodes.Select(n => n.Type.ToString()).Distinct().OrderBy(t => t);
        foreach (var typeName in distinctTypes)
        {
            var filter = new EntityTypeFilter
            {
                TypeName = typeName,
                IsSelected = existingTypes.GetValueOrDefault(typeName, true)
            };
            filter.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(EntityTypeFilter.IsSelected))
                {
                    ApplyFilterAndLayout();
                }
            };
            EntityTypeFilters.Add(filter);
        }
    }

    private void ApplyFilterAndLayout()
    {
        StopSimulation();

        var selectedTypes = EntityTypeFilters
            .Where(f => f.IsSelected)
            .Select(f => f.TypeName)
            .ToHashSet();

        var filtered = selectedTypes.Count == 0
            ? new List<GraphNode>()
            : _allNodes.Where(n => selectedTypes.Contains(n.Type.ToString())).ToList();

        Nodes.Clear();
        Edges.Clear();

        foreach (var node in filtered)
        {
            Nodes.Add(node);
        }

        var visibleIds = Nodes.Select(n => n.Id).ToHashSet();
        foreach (var edge in _allEdges.Where(e => visibleIds.Contains(e.SourceId) && visibleIds.Contains(e.TargetId)))
        {
            Edges.Add(edge);
        }

        if (Nodes.Count == 0) return;

        StartForceDirectedLayout();
    }

    private void StartForceDirectedLayout()
    {
        _layout = new ForceDirectedLayout(CanvasWidth, CanvasHeight);

        // Build layout data structures
        var nodeIndexMap = new Dictionary<string, int>();
        _layoutNodes = new List<LayoutNode>(Nodes.Count);
        for (int i = 0; i < Nodes.Count; i++)
        {
            nodeIndexMap[Nodes[i].Id] = i;
            _layoutNodes.Add(new LayoutNode { IsPinned = Nodes[i].IsPinned });
        }

        _layout.InitializePositions(_layoutNodes);

        // Sync initial positions back to GraphNodes
        for (int i = 0; i < Nodes.Count; i++)
        {
            Nodes[i].X = _layoutNodes[i].X;
            Nodes[i].Y = _layoutNodes[i].Y;
        }

        _layoutEdges = new List<LayoutEdge>();
        foreach (var edge in Edges)
        {
            if (nodeIndexMap.TryGetValue(edge.SourceId, out int srcIdx) &&
                nodeIndexMap.TryGetValue(edge.TargetId, out int tgtIdx))
            {
                _layoutEdges.Add(new LayoutEdge(srcIdx, tgtIdx));
            }
        }

        _temperature = CanvasWidth / 10.0;
        _iteration = 0;
        IsSimulating = true;

        _layoutTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _layoutTimer.Tick += LayoutTimerTick;
        _layoutTimer.Start();
    }

    private void LayoutTimerTick(object? sender, EventArgs e)
    {
        if (_layout is null || _layoutNodes is null || _layoutEdges is null ||
            ForceDirectedLayout.IsConverged(_temperature, _iteration))
        {
            StopSimulation();
            return;
        }

        _temperature = _layout.Step(_layoutNodes, _layoutEdges, _temperature);
        _iteration++;

        // Sync layout positions back to GraphNodes for rendering
        for (int i = 0; i < Nodes.Count && i < _layoutNodes.Count; i++)
        {
            Nodes[i].X = _layoutNodes[i].X;
            Nodes[i].Y = _layoutNodes[i].Y;
        }

        LayoutUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void StopSimulation()
    {
        _layoutTimer?.Stop();
        _layoutTimer = null;
        IsSimulating = false;
    }

    /// <summary>
    /// Called by GraphCanvas when a node is being dragged.
    /// Updates both the GraphNode and the corresponding LayoutNode position.
    /// </summary>
    public void UpdateNodePosition(GraphNode node, double x, double y)
    {
        node.X = x;
        node.Y = y;
        node.IsPinned = true;

        if (_layoutNodes is null) return;
        int index = -1;
        for (int i = 0; i < Nodes.Count; i++)
        {
            if (ReferenceEquals(Nodes[i], node))
            {
                index = i;
                break;
            }
        }

        if (index >= 0 && index < _layoutNodes.Count)
        {
            _layoutNodes[index].X = x;
            _layoutNodes[index].Y = y;
            _layoutNodes[index].IsPinned = true;
        }
    }
}
