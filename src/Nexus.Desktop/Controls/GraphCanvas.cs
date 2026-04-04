using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Nexus.Desktop.ViewModels;
using Nexus.Memory.Models;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Nexus.Desktop.Controls;

public class GraphCanvas : Control
{
    private static readonly ImmutableSolidColorBrush s_bgBrush = new(Color.Parse("#1E1E2E"));
    private static readonly ImmutablePen s_edgePen = new(new ImmutableSolidColorBrush(Color.Parse("#45475A")), 1);
    private static readonly ImmutableSolidColorBrush s_edgeLabelBrush = new(Color.Parse("#6C7086"));
    private static readonly ImmutablePen s_selectedNodePen = new(new ImmutableSolidColorBrush(Colors.White), 2);
    private static readonly ImmutablePen s_pinnedNodePen = new(new ImmutableSolidColorBrush(Color.Parse("#F9E2AF")), 1.5);

    private static readonly ImmutableSolidColorBrush s_personBrush = new(Color.Parse("#89B4FA"));
    private static readonly ImmutableSolidColorBrush s_projectBrush = new(Color.Parse("#A6E3A1"));
    private static readonly ImmutableSolidColorBrush s_technologyBrush = new(Color.Parse("#FAB387"));
    private static readonly ImmutableSolidColorBrush s_decisionBrush = new(Color.Parse("#F38BA8"));
    private static readonly ImmutableSolidColorBrush s_dateBrush = new(Color.Parse("#BAC2DE"));
    private static readonly ImmutableSolidColorBrush s_preferenceBrush = new(Color.Parse("#CBA6F7"));
    private static readonly ImmutableSolidColorBrush s_defaultTypeBrush = new(Color.Parse("#6C7086"));

    public static readonly StyledProperty<ObservableCollection<GraphNode>?> NodesProperty =
        AvaloniaProperty.Register<GraphCanvas, ObservableCollection<GraphNode>?>(nameof(Nodes));

    public static readonly StyledProperty<ObservableCollection<GraphEdge>?> EdgesProperty =
        AvaloniaProperty.Register<GraphCanvas, ObservableCollection<GraphEdge>?>(nameof(Edges));

    public static readonly StyledProperty<GraphNode?> SelectedNodeProperty =
        AvaloniaProperty.Register<GraphCanvas, GraphNode?>(nameof(SelectedNode),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public ObservableCollection<GraphNode>? Nodes
    {
        get => GetValue(NodesProperty);
        set => SetValue(NodesProperty, value);
    }

    public ObservableCollection<GraphEdge>? Edges
    {
        get => GetValue(EdgesProperty);
        set => SetValue(EdgesProperty, value);
    }

    public GraphNode? SelectedNode
    {
        get => GetValue(SelectedNodeProperty);
        set => SetValue(SelectedNodeProperty, value);
    }

    private double _offsetX, _offsetY, _scale = 1.0;
    private bool _isPanning;
    private Point _lastPanPoint;
    private GraphNode? _draggingNode;
    private Point _dragOffset;
    private Dictionary<string, GraphNode>? _nodeLookup;

    static GraphCanvas()
    {
        AffectsRender<GraphCanvas>(NodesProperty, EdgesProperty, SelectedNodeProperty);

        NodesProperty.Changed.AddClassHandler<GraphCanvas>((canvas, args) =>
        {
            canvas._nodeLookup = null;

            if (args.OldValue is ObservableCollection<GraphNode> oldCollection)
            {
                oldCollection.CollectionChanged -= canvas.OnNodesCollectionChanged;
            }

            if (args.NewValue is ObservableCollection<GraphNode> newCollection)
            {
                newCollection.CollectionChanged += canvas.OnNodesCollectionChanged;
            }
        });
    }

    private void OnNodesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _nodeLookup = null;
    }

    public GraphCanvas()
    {
        ClipToBounds = true;
    }

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);

        ctx.FillRectangle(s_bgBrush, new Rect(Bounds.Size));

        var nodes = Nodes;
        var edges = Edges;

        if (nodes == null || nodes.Count == 0)
        {
            var ft = new FormattedText(
                "No entities in memory yet. Start chatting to build your knowledge graph!",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default, 14, Brushes.Gray);
            ctx.DrawText(ft, new Point(Bounds.Width / 2 - ft.Width / 2, Bounds.Height / 2));
            return;
        }

        using var transform = ctx.PushTransform(
            Matrix.CreateTranslation(_offsetX, _offsetY) * Matrix.CreateScale(_scale, _scale));

        if (edges != null)
        {
            _nodeLookup ??= nodes.ToDictionary(n => n.Id);

            foreach (var edge in edges)
            {
                if (!_nodeLookup.TryGetValue(edge.SourceId, out var src) ||
                    !_nodeLookup.TryGetValue(edge.TargetId, out var tgt))
                    continue;

                ctx.DrawLine(s_edgePen, new Point(src.X, src.Y), new Point(tgt.X, tgt.Y));

                var midX = (src.X + tgt.X) / 2;
                var midY = (src.Y + tgt.Y) / 2;
                var labelFt = new FormattedText(edge.RelationType,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    Typeface.Default, 9, s_edgeLabelBrush);
                ctx.DrawText(labelFt, new Point(midX - labelFt.Width / 2, midY - labelFt.Height / 2));
            }
        }

        foreach (var node in nodes)
        {
            var typeBrush = GetNodeBrush(node.Type);
            var isSelected = SelectedNode?.Id == node.Id;
            var isPinned = node.IsPinned;
            var radius = node.Size / 2;

            IBrush fillBrush = isSelected
                ? typeBrush
                : new ImmutableSolidColorBrush(typeBrush.Color, 0.85);
            var pen = isSelected
                ? s_selectedNodePen
                : isPinned
                    ? s_pinnedNodePen
                    : null;
            ctx.DrawEllipse(fillBrush, pen, new Point(node.X, node.Y), radius, radius);

            var label = node.Name.Length > 12 ? node.Name[..12] + "\u2026" : node.Name;
            var nodeFt = new FormattedText(label,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default, 11, Brushes.White);
            ctx.DrawText(nodeFt, new Point(node.X - nodeFt.Width / 2, node.Y + radius + 2));
        }
    }

    private static ImmutableSolidColorBrush GetNodeBrush(EntityType type) => type switch
    {
        EntityType.Person => s_personBrush,
        EntityType.Project => s_projectBrush,
        EntityType.Technology => s_technologyBrush,
        EntityType.Decision => s_decisionBrush,
        EntityType.Date => s_dateBrush,
        EntityType.Preference => s_preferenceBrush,
        _ => s_defaultTypeBrush
    };

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pt = e.GetPosition(this);

        var hit = HitTest(pt);
        if (hit != null)
        {
            SelectedNode = hit;

            // Start dragging
            var graphPt = ScreenToGraph(pt);
            _draggingNode = hit;
            _dragOffset = new Point(graphPt.X - hit.X, graphPt.Y - hit.Y);
            e.Pointer.Capture(this);
            return;
        }

        _isPanning = true;
        _lastPanPoint = pt;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_draggingNode != null)
        {
            var pt = e.GetPosition(this);
            var graphPt = ScreenToGraph(pt);
            double newX = graphPt.X - _dragOffset.X;
            double newY = graphPt.Y - _dragOffset.Y;

            var vm = DataContext as MemoryGraphViewModel;
            vm?.UpdateNodePosition(_draggingNode, newX, newY);
            InvalidateVisual();
            return;
        }

        if (_isPanning)
        {
            var pt = e.GetPosition(this);
            _offsetX += pt.X - _lastPanPoint.X;
            _offsetY += pt.Y - _lastPanPoint.Y;
            _lastPanPoint = pt;
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _draggingNode = null;
        _isPanning = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var delta = e.Delta.Y > 0 ? 1.1 : 0.9;
        _scale = Math.Clamp(_scale * delta, 0.1, 5.0);
        InvalidateVisual();
    }

    private Point ScreenToGraph(Point screenPt) => new(
        (screenPt.X - _offsetX) / _scale,
        (screenPt.Y - _offsetY) / _scale);

    private GraphNode? HitTest(Point screenPt)
    {
        var nodes = Nodes;
        if (nodes == null) return null;

        var graphPt = ScreenToGraph(screenPt);

        return nodes.FirstOrDefault(n =>
        {
            var dx = graphPt.X - n.X;
            var dy = graphPt.Y - n.Y;
            return Math.Sqrt(dx * dx + dy * dy) <= n.Size / 2 + 4;
        });
    }
}
