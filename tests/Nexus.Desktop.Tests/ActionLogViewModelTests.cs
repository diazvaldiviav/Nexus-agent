using Nexus.Desktop.Tests.Fakes;
using Nexus.Desktop.ViewModels;
using Nexus.Memory.Abstractions;
using Nexus.Memory.Models;

namespace Nexus.Desktop.Tests;

public class ActionLogViewModelTests
{
    private class TestableActionLogViewModel : ActionLogViewModel
    {
        public TestableActionLogViewModel(IKnowledgeGraph graph, IActionLogNotifier notifier)
            : base(graph, notifier) { }

        protected override void DispatchToUI(Action action) => action();
    }

    [Fact]
    public async Task LoadActionsAsync_PopulatesActions()
    {
        // Arrange
        var graph = new FakeKnowledgeGraph
        {
            AgentActions = new List<AgentAction>
            {
                new() { ActionType = "chat", Detail = "Test 1" },
                new() { ActionType = "chat", Detail = "Test 2" },
                new() { ActionType = "entity_extraction", Detail = "Test 3" }
            }
        };
        var vm = new ActionLogViewModel(graph, graph);

        // Act
        await vm.LoadActionsAsync();

        // Assert
        Assert.Equal(3, vm.Actions.Count);
    }

    [Fact]
    public async Task LoadActionsAsync_FiltersByType()
    {
        // Arrange
        var graph = new FakeKnowledgeGraph
        {
            AgentActions = new List<AgentAction>
            {
                new() { ActionType = "chat", Detail = "Chat action" },
                new() { ActionType = "entity_extraction", Detail = "Extract action" },
                new() { ActionType = "chat", Detail = "Another chat" }
            }
        };
        var vm = new ActionLogViewModel(graph, graph);

        // Act
        vm.FilterType = "chat";
        await vm.LoadActionsAsync();

        // Assert
        Assert.All(vm.Actions, a => Assert.Equal("chat", a.ActionType));
        Assert.Equal(2, vm.Actions.Count);
    }

    [Fact]
    public async Task LoadActionsAsync_SetsIsLoading()
    {
        // Arrange
        var graph = new FakeKnowledgeGraph
        {
            AgentActions = new List<AgentAction>
            {
                new() { ActionType = "chat" }
            }
        };
        var vm = new ActionLogViewModel(graph, graph);
        var isLoadingValues = new List<bool>();
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ActionLogViewModel.IsLoading))
                isLoadingValues.Add(vm.IsLoading);
        };

        // Act
        await vm.LoadActionsAsync();

        // Assert
        Assert.Contains(true, isLoadingValues);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task FilterTypeChanged_TriggersReload()
    {
        // Arrange
        var graph = new FakeKnowledgeGraph
        {
            AgentActions = new List<AgentAction>
            {
                new() { ActionType = "chat" },
                new() { ActionType = "entity_extraction" }
            }
        };
        var vm = new ActionLogViewModel(graph, graph);
        await vm.LoadActionsAsync();
        var initialCallCount = graph.GetRecentActionsCallCount;

        // Act
        vm.FilterType = "chat";

        // Allow the fire-and-forget task from OnFilterTypeChanged to complete.
        // Task.Delay is needed because OnFilterTypeChanged uses _ = LoadActionsAsync() (fire-and-forget).
        await Task.Delay(100);

        // Assert
        Assert.True(graph.GetRecentActionsCallCount >= initialCallCount + 1);
    }

    [Fact]
    public async Task HasActions_FalseInitially_TrueAfterLoad()
    {
        // Arrange
        var graph = new FakeKnowledgeGraph
        {
            AgentActions = new List<AgentAction>
            {
                new() { ActionType = "chat", Detail = "Test action" }
            }
        };
        var vm = new ActionLogViewModel(graph, graph);

        // Assert -- initially false
        Assert.False(vm.HasActions);

        // Act
        await vm.LoadActionsAsync();

        // Assert -- true after load
        Assert.True(vm.HasActions);
    }

    [Fact]
    public async Task RealTimeAction_AppearsAtTop()
    {
        // Arrange
        var graph = new FakeKnowledgeGraph();
        var vm = new TestableActionLogViewModel(graph, graph);
        var action = new AgentAction { ActionType = "chat", Detail = "Hello" };

        // Act
        await graph.LogActionAsync(action);

        // Assert
        Assert.Single(vm.Actions);
        Assert.Equal("chat", vm.Actions[0].ActionType);
    }

    [Fact]
    public async Task RealTimeAction_FilteredOut_NotAdded()
    {
        // Arrange
        var graph = new FakeKnowledgeGraph();
        var vm = new TestableActionLogViewModel(graph, graph);
        vm.FilterType = "chat";

        // Allow fire-and-forget LoadActionsAsync from filter change to complete
        await Task.Delay(50);

        var action = new AgentAction { ActionType = "decay", Detail = "Decay event" };

        // Act
        await graph.LogActionAsync(action);

        // Assert
        Assert.Empty(vm.Actions);
    }

    [Fact]
    public async Task Dispose_UnsubscribesFromEvent()
    {
        // Arrange
        var graph = new FakeKnowledgeGraph();
        var vm = new TestableActionLogViewModel(graph, graph);
        vm.Dispose();

        // Act
        await graph.LogActionAsync(new AgentAction { ActionType = "chat", Detail = "After dispose" });

        // Assert
        Assert.Empty(vm.Actions);
    }

    [Fact]
    public async Task RealTimeAction_AllFilter_AlwaysAdded()
    {
        // Arrange
        var graph = new FakeKnowledgeGraph();
        var vm = new TestableActionLogViewModel(graph, graph);
        // FilterType defaults to "All"

        // Act
        await graph.LogActionAsync(new AgentAction { ActionType = "decay", Detail = "Decay" });
        await graph.LogActionAsync(new AgentAction { ActionType = "summarize", Detail = "Summary" });
        await graph.LogActionAsync(new AgentAction { ActionType = "chat", Detail = "Chat" });

        // Assert
        Assert.Equal(3, vm.Actions.Count);
    }
}
