using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nexus.Desktop.ViewModels;
using Nexus.Memory.Models;
using System.Collections.ObjectModel;

namespace Nexus.Desktop.Controls;

public class GraphCanvas : Control
{
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

    static GraphCanvas()
    {
        AffectsRender<GraphCanvas>(NodesProperty, EdgesProperty, SelectedNodeProperty);
    }

    public GraphCanvas()
    {
        ClipToBounds = true;
    }

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);

        ctx.FillRectangle(new SolidColorBrush(Color.Parse("#1E1E2E")), new Rect(Bounds.Size));

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
            var nodeLookup = nodes.ToDictionary(n => n.Id);
            var edgePen = new Pen(new SolidColorBrush(Color.Parse("#45475A")), 1);

            foreach (var edge in edges)
            {
                if (!nodeLookup.TryGetValue(edge.SourceId, out var src) ||
                    !nodeLookup.TryGetValue(edge.TargetId, out var tgt))
                    continue;

                ctx.DrawLine(edgePen, new Point(src.X, src.Y), new Point(tgt.X, tgt.Y));

                var midX = (src.X + tgt.X) / 2;
                var midY = (src.Y + tgt.Y) / 2;
                var labelFt = new FormattedText(edge.RelationType,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    Typeface.Default, 9, new SolidColorBrush(Color.Parse("#6C7086")));
                ctx.DrawText(labelFt, new Point(midX - labelFt.Width / 2, midY - labelFt.Height / 2));
            }
        }

        foreach (var node in nodes)
        {
            var color = GetNodeColor(node.Type);
            var isSelected = SelectedNode?.Id == node.Id;
            var radius = node.Size / 2;

            var brush = new SolidColorBrush(color, isSelected ? 1.0 : 0.85);
            ctx.DrawEllipse(brush,
                isSelected ? new Pen(Brushes.White, 2) : null,
                new Point(node.X, node.Y), radius, radius);

            var label = node.Name.Length > 12 ? node.Name[..12] + "…" : node.Name;
            var nodeFt = new FormattedText(label,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default, 11, Brushes.White);
            ctx.DrawText(nodeFt, new Point(node.X - nodeFt.Width / 2, node.Y + radius + 2));
        }
    }

    private static Color GetNodeColor(EntityType type) => type switch
    {
        EntityType.Person => Color.Parse("#89B4FA"),
        EntityType.Project => Color.Parse("#A6E3A1"),
        EntityType.Technology => Color.Parse("#FAB387"),
        EntityType.Decision => Color.Parse("#F38BA8"),
        EntityType.Date => Color.Parse("#BAC2DE"),
        EntityType.Preference => Color.Parse("#CBA6F7"),
        _ => Color.Parse("#6C7086")
    };

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pt = e.GetPosition(this);

        var hit = HitTest(pt);
        if (hit != null)
        {
            SelectedNode = hit;
            return;
        }

        _isPanning = true;
        _lastPanPoint = pt;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
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

    private GraphNode? HitTest(Point screenPt)
    {
        var nodes = Nodes;
        if (nodes == null) return null;

        var graphPt = new Point(
            (screenPt.X - _offsetX) / _scale,
            (screenPt.Y - _offsetY) / _scale);

        return nodes.FirstOrDefault(n =>
        {
            var dx = graphPt.X - n.X;
            var dy = graphPt.Y - n.Y;
            return Math.Sqrt(dx * dx + dy * dy) <= n.Size / 2 + 4;
        });
    }
}
