namespace Nexus.Desktop.Layout;

/// <summary>
/// Mutable position data for a single node in the force-directed layout.
/// Decoupled from GraphNode so the algorithm can be tested without Avalonia.
/// </summary>
public class LayoutNode
{
    public double X { get; set; }
    public double Y { get; set; }
    public bool IsPinned { get; set; }

    /// <summary>Accumulated displacement for the current iteration.</summary>
    internal double Dx { get; set; }
    internal double Dy { get; set; }
}

/// <summary>
/// An edge between two nodes, identified by index into the node list.
/// </summary>
public readonly record struct LayoutEdge(int SourceIndex, int TargetIndex);

/// <summary>
/// Fruchterman-Reingold force-directed graph layout algorithm.
/// Computes repulsion between all node pairs and attraction along edges,
/// with temperature-based cooling for convergence.
/// </summary>
public class ForceDirectedLayout
{
    private readonly double _width;
    private readonly double _height;
    private const double Jitter = 0.01;
    private const double MinTemperature = 0.5;
    private const int DefaultMaxIterations = 300;
    private const double CoolingFactor = 0.95;

    public ForceDirectedLayout(double width, double height)
    {
        _width = width;
        _height = height;
    }

    /// <summary>
    /// Initialize node positions in a grid-like random distribution within bounds.
    /// Uses a seeded RNG for deterministic results.
    /// </summary>
    public void InitializePositions(IList<LayoutNode> nodes, int seed = 42)
    {
        var rng = new Random(seed);
        var margin = 50.0;
        var usableWidth = _width - 2 * margin;
        var usableHeight = _height - 2 * margin;

        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].IsPinned) continue;
            nodes[i].X = margin + rng.NextDouble() * usableWidth;
            nodes[i].Y = margin + rng.NextDouble() * usableHeight;
        }
    }

    /// <summary>
    /// Performs one iteration of the Fruchterman-Reingold algorithm.
    /// Returns the temperature after cooling.
    /// </summary>
    public double Step(IList<LayoutNode> nodes, IList<LayoutEdge> edges, double temperature)
    {
        int count = nodes.Count;
        if (count == 0) return 0;

        double area = _width * _height;
        double k = Math.Sqrt(area / count);

        // Reset displacements
        for (int i = 0; i < count; i++)
        {
            nodes[i].Dx = 0;
            nodes[i].Dy = 0;
        }

        // Repulsion: every pair pushes apart with force k^2 / distance
        for (int i = 0; i < count; i++)
        {
            for (int j = i + 1; j < count; j++)
            {
                double dx = nodes[i].X - nodes[j].X;
                double dy = nodes[i].Y - nodes[j].Y;
                double distance = Math.Sqrt(dx * dx + dy * dy);

                if (distance < Jitter)
                {
                    dx = Jitter;
                    dy = Jitter;
                    distance = Math.Sqrt(dx * dx + dy * dy);
                }

                double force = (k * k) / distance;
                double fx = (dx / distance) * force;
                double fy = (dy / distance) * force;

                nodes[i].Dx += fx;
                nodes[i].Dy += fy;
                nodes[j].Dx -= fx;
                nodes[j].Dy -= fy;
            }
        }

        // Attraction: edges pull connected nodes together with force distance^2 / k
        for (int e = 0; e < edges.Count; e++)
        {
            var edge = edges[e];
            if (edge.SourceIndex < 0 || edge.SourceIndex >= count ||
                edge.TargetIndex < 0 || edge.TargetIndex >= count)
                continue;

            var src = nodes[edge.SourceIndex];
            var tgt = nodes[edge.TargetIndex];

            double dx = src.X - tgt.X;
            double dy = src.Y - tgt.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);

            if (distance < Jitter) continue;

            double force = (distance * distance) / k;
            double fx = (dx / distance) * force;
            double fy = (dy / distance) * force;

            src.Dx -= fx;
            src.Dy -= fy;
            tgt.Dx += fx;
            tgt.Dy += fy;
        }

        // Apply displacements, clamped by temperature. Skip pinned nodes.
        for (int i = 0; i < count; i++)
        {
            if (nodes[i].IsPinned) continue;

            double dx = nodes[i].Dx;
            double dy = nodes[i].Dy;
            double displacement = Math.Sqrt(dx * dx + dy * dy);

            if (displacement > 0)
            {
                double clampedDisp = Math.Min(displacement, temperature);
                nodes[i].X += (dx / displacement) * clampedDisp;
                nodes[i].Y += (dy / displacement) * clampedDisp;
            }

            // Clamp within bounds
            nodes[i].X = Math.Clamp(nodes[i].X, 0, _width);
            nodes[i].Y = Math.Clamp(nodes[i].Y, 0, _height);
        }

        return temperature * CoolingFactor;
    }

    /// <summary>
    /// Returns true when the simulation should stop: temperature too low or max iterations reached.
    /// </summary>
    public static bool IsConverged(double temperature, int iteration, int maxIterations = DefaultMaxIterations)
        => temperature < MinTemperature || iteration >= maxIterations;
}
