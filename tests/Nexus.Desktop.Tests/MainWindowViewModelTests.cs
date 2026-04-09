using Microsoft.Extensions.DependencyInjection;
using Nexus.Connectors;
using Nexus.Core;
using Nexus.Core.Abstractions;
using Nexus.Core.Config;
using Nexus.Desktop.Tests.Fakes;
using Nexus.Desktop.ViewModels;
using Nexus.Memory.Abstractions;

namespace Nexus.Desktop.Tests;

public class MainWindowViewModelTests
{
    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        var fakeKg = new FakeKnowledgeGraph();
        services.AddSingleton<IKnowledgeGraph>(fakeKg);
        services.AddSingleton<IActionLogNotifier>(fakeKg);
        services.AddSingleton<IAgentService>(new FakeAgentService());
        services.AddSingleton(new NexusConfig());
        services.AddSingleton(new McpLifecycleService(new FakeMcpClientManager(), new ToolRegistry()));
        services.AddTransient<ChatViewModel>();
        services.AddTransient<MemoryGraphViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddSingleton<ActionLogViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Constructor_DefaultsToChat()
    {
        // Arrange & Act
        using var sp = BuildServiceProvider();
        var vm = sp.GetRequiredService<MainWindowViewModel>();

        // Assert
        Assert.Equal("chat", vm.ActiveTab);
        Assert.IsType<ChatViewModel>(vm.CurrentView);
    }

    [Fact]
    public void NavigateToMemoryGraph_SetsActiveTabAndView()
    {
        // Arrange
        using var sp = BuildServiceProvider();
        var vm = sp.GetRequiredService<MainWindowViewModel>();

        // Act
        vm.NavigateToMemoryGraphCommand.Execute(null);

        // Assert
        Assert.Equal("memory", vm.ActiveTab);
        Assert.IsType<MemoryGraphViewModel>(vm.CurrentView);
    }

    [Fact]
    public void NavigateToSettings_SetsActiveTabAndView()
    {
        // Arrange
        using var sp = BuildServiceProvider();
        var vm = sp.GetRequiredService<MainWindowViewModel>();

        // Act
        vm.NavigateToSettingsCommand.Execute(null);

        // Assert
        Assert.Equal("settings", vm.ActiveTab);
        Assert.IsType<SettingsViewModel>(vm.CurrentView);
    }

    [Fact]
    public void NavigateToActionLog_SetsActiveTabAndView_TriggersLoad()
    {
        // Arrange
        using var sp = BuildServiceProvider();
        var fakeKg = sp.GetRequiredService<IKnowledgeGraph>() as FakeKnowledgeGraph;
        Assert.NotNull(fakeKg);
        var callCountBefore = fakeKg.GetRecentActionsCallCount;
        var vm = sp.GetRequiredService<MainWindowViewModel>();

        // Act
        vm.NavigateToActionLogCommand.Execute(null);

        // Assert
        Assert.Equal("log", vm.ActiveTab);
        Assert.IsType<ActionLogViewModel>(vm.CurrentView);
        Assert.True(fakeKg.GetRecentActionsCallCount > callCountBefore,
            "LoadActionsAsync should call GetRecentActionsAsync on navigation");
    }
}
