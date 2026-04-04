using Nexus.Desktop.Layout;

namespace Nexus.Core.Tests;

public class ForceDirectedLayoutTests
{
    [Fact]
    public void Step_TwoDisconnectedNodes_RepelEachOther()
    {
        // Arrange
        var layout = new ForceDirectedLayout(800, 600);
        var nodes = new List<LayoutNode>
        {
            new() { X = 400, Y = 300 },
            new() { X = 401, Y = 300 }
        };
        var edges = new List<LayoutEdge>();
        double temperature = 80.0;

        double initialDistance = Math.Abs(nodes[0].X - nodes[1].X);

        // Act — run several iterations
        for (int i = 0; i < 50; i++)
        {
            temperature = layout.Step(nodes, edges, temperature);
        }

        double finalDistance = Math.Sqrt(
            Math.Pow(nodes[0].X - nodes[1].X, 2) +
            Math.Pow(nodes[0].Y - nodes[1].Y, 2));

        // Assert — nodes should be farther apart after repulsion
        Assert.True(finalDistance > initialDistance,
            $"Expected final distance ({finalDistance:F2}) > initial distance ({initialDistance:F2})");
    }

    [Fact]
    public void Step_ConnectedNodes_ConvergeToStableDistance()
    {
        // Arrange
        var layout = new ForceDirectedLayout(800, 600);
        var nodes = new List<LayoutNode>
        {
            new() { X = 100, Y = 300 },
            new() { X = 700, Y = 300 }
        };
        var edges = new List<LayoutEdge> { new(0, 1) };
        double temperature = 80.0;

        double initialDistance = Math.Abs(nodes[0].X - nodes[1].X);

        // Act — run to convergence
        for (int i = 0; i < 300; i++)
        {
            temperature = layout.Step(nodes, edges, temperature);
        }

        double finalDistance = Math.Sqrt(
            Math.Pow(nodes[0].X - nodes[1].X, 2) +
            Math.Pow(nodes[0].Y - nodes[1].Y, 2));

        // Assert — connected nodes at extreme distance should come closer
        Assert.True(finalDistance < initialDistance,
            $"Expected final distance ({finalDistance:F2}) < initial distance ({initialDistance:F2})");
    }

    [Fact]
    public void Step_PinnedNode_DoesNotMove()
    {
        // Arrange
        var layout = new ForceDirectedLayout(800, 600);
        var nodes = new List<LayoutNode>
        {
            new() { X = 400, Y = 300, IsPinned = true },
            new() { X = 405, Y = 300 }
        };
        var edges = new List<LayoutEdge> { new(0, 1) };
        double temperature = 80.0;

        // Act
        for (int i = 0; i < 50; i++)
        {
            temperature = layout.Step(nodes, edges, temperature);
        }

        // Assert — pinned node stays exactly at original position
        Assert.Equal(400, nodes[0].X);
        Assert.Equal(300, nodes[0].Y);
    }

    [Fact]
    public void IsConverged_LowTemperature_ReturnsTrue()
    {
        // Arrange / Act / Assert
        Assert.True(ForceDirectedLayout.IsConverged(0.1, 10));
    }

    [Fact]
    public void IsConverged_MaxIterationsReached_ReturnsTrue()
    {
        // Arrange / Act / Assert
        Assert.True(ForceDirectedLayout.IsConverged(50.0, 300));
    }

    [Fact]
    public void IsConverged_HighTemperatureLowIteration_ReturnsFalse()
    {
        // Arrange / Act / Assert
        Assert.False(ForceDirectedLayout.IsConverged(50.0, 10));
    }

    [Fact]
    public void Step_100Nodes_ConvergesWithinTwoSeconds()
    {
        // Arrange
        var layout = new ForceDirectedLayout(800, 600);
        var nodes = new List<LayoutNode>();
        for (int i = 0; i < 100; i++)
        {
            nodes.Add(new LayoutNode());
        }
        layout.InitializePositions(nodes);

        // Create a connected chain of edges
        var edges = new List<LayoutEdge>();
        for (int i = 0; i < 99; i++)
        {
            edges.Add(new LayoutEdge(i, i + 1));
        }

        double temperature = 80.0;
        int iteration = 0;

        // Act
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!ForceDirectedLayout.IsConverged(temperature, iteration))
        {
            temperature = layout.Step(nodes, edges, temperature);
            iteration++;
        }
        sw.Stop();

        // Assert — must converge within 2 seconds (AC-3)
        Assert.True(sw.ElapsedMilliseconds < 2000,
            $"Layout took {sw.ElapsedMilliseconds}ms, expected < 2000ms");
        Assert.True(iteration > 0, "Should have run at least one iteration");
    }

    [Fact]
    public void Step_EmptyNodeList_DoesNotThrow()
    {
        // Arrange
        var layout = new ForceDirectedLayout(800, 600);
        var nodes = new List<LayoutNode>();
        var edges = new List<LayoutEdge>();
        double temperature = 80.0;

        // Act
        var result = layout.Step(nodes, edges, temperature);

        // Assert
        Assert.True(result >= 0, $"Expected result >= 0 but got {result}");
    }

    [Fact]
    public void InitializePositions_SkipsPinnedNodes()
    {
        // Arrange
        var layout = new ForceDirectedLayout(800, 600);
        var nodes = new List<LayoutNode>
        {
            new() { X = 100, Y = 200, IsPinned = true },
            new() { X = 0, Y = 0 }
        };

        // Act
        layout.InitializePositions(nodes);

        // Assert — pinned node unchanged, unpinned node repositioned
        Assert.Equal(100, nodes[0].X);
        Assert.Equal(200, nodes[0].Y);
        Assert.NotEqual(0, nodes[1].X);
    }
}
