using Avalonia.Headless.XUnit;
using Nexus.Desktop.Tests.Fakes;
using Nexus.Desktop.ViewModels;
using Nexus.Memory.Models;

namespace Nexus.Desktop.Tests;

public class MemoryGraphViewModelTests
{
    [Fact]
    public void SelectNode_SetsSelectedNodeAndDetails()
    {
        // Arrange
        var graph = new FakeKnowledgeGraph();
        var vm = new MemoryGraphViewModel(graph);
        var node = new GraphNode
        {
            Id = "1",
            Name = "TestEntity",
            Type = EntityType.Technology,
            RelevanceScore = 0.9,
            Summary = "A test entity"
        };

        // Act
        vm.SelectNode(node);

        // Assert
        Assert.Equal(node, vm.SelectedNode);
        Assert.Contains("TestEntity", vm.SelectedNodeDetails);
    }

    [Fact]
    public void SelectNode_Null_ClearsDetails()
    {
        // Arrange
        var graph = new FakeKnowledgeGraph();
        var vm = new MemoryGraphViewModel(graph);
        var node = new GraphNode { Id = "1", Name = "Test" };
        vm.SelectNode(node);

        // Act
        vm.SelectNode(null);

        // Assert
        Assert.Null(vm.SelectedNode);
        Assert.Equal(string.Empty, vm.SelectedNodeDetails);
    }

    [AvaloniaFact]
    public async Task LoadGraphAsync_PopulatesNodesAndEdges()
    {
        // Arrange
        var entity1 = new Entity { Id = "e1", Name = "Entity1", Type = EntityType.Person };
        var entity2 = new Entity { Id = "e2", Name = "Entity2", Type = EntityType.Technology };
        var relation = new Relation { EntityId1 = "e1", EntityId2 = "e2", RelationType = "uses" };
        var graph = new FakeKnowledgeGraph
        {
            Entities = new List<Entity> { entity1, entity2 },
            Relations = new List<Relation> { relation }
        };
        var vm = new MemoryGraphViewModel(graph);

        // Act
        await vm.LoadGraphAsync();

        // Assert
        Assert.Equal(2, vm.Nodes.Count);
        Assert.Single(vm.Edges);
    }

    [AvaloniaFact]
    public async Task LoadGraphAsync_EmptyData_YieldsEmptyCollections()
    {
        // Arrange
        var graph = new FakeKnowledgeGraph();
        var vm = new MemoryGraphViewModel(graph);

        // Act
        await vm.LoadGraphAsync();

        // Assert
        Assert.Empty(vm.Nodes);
        Assert.Empty(vm.Edges);
        Assert.False(vm.HasNodes);
    }

    [AvaloniaFact]
    public async Task HasNodes_AfterLoad_ReturnsTrue()
    {
        // Arrange
        var graph = new FakeKnowledgeGraph
        {
            Entities = new List<Entity>
            {
                new() { Id = "e1", Name = "E1", Type = EntityType.Person }
            }
        };
        var vm = new MemoryGraphViewModel(graph);
        Assert.False(vm.HasNodes);

        // Act
        await vm.LoadGraphAsync();

        // Assert
        Assert.True(vm.HasNodes);
    }
}
