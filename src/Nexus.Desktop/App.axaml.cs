using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.Connectors;
using Nexus.Core;
using Nexus.Core.Config;
using Nexus.Desktop.ViewModels;
using Nexus.Desktop.Views;
using Nexus.Memory.Abstractions;
using Nexus.Memory.Models;

namespace Nexus.Desktop;

public partial class App : Application
{
    public static IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var config = ConfigLoader.Load();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        serviceCollection.AddNexusAgent(config);
        serviceCollection.AddNexusMcp();
        serviceCollection.AddSingleton<MainWindowViewModel>();
        serviceCollection.AddTransient<ChatViewModel>();
        serviceCollection.AddTransient<MemoryGraphViewModel>();
        serviceCollection.AddTransient<SettingsViewModel>();
        serviceCollection.AddSingleton<ActionLogViewModel>();
        Services = serviceCollection.BuildServiceProvider();
        StartMcpAutoConnect(Services, config);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void StartMcpAutoConnect(IServiceProvider services, NexusConfig config)
    {
        if (config.Mcp.Servers.Count == 0) return;

        _ = Task.Run(async () =>
        {
            var lifecycle = services.GetRequiredService<McpLifecycleService>();
            var graph = services.GetService<IKnowledgeGraph>();
            await lifecycle.ConnectServersAsync(
                config.Mcp.Servers,
                actionLogger: graph is null ? null : (evt, ct) => LogMcpActionAsync(graph, evt, ct))
                .ConfigureAwait(false);
        });
    }

    private static Task LogMcpActionAsync(IKnowledgeGraph graph, McpLifecycleEvent evt, CancellationToken ct)
    {
        var detail = evt.EventType switch
        {
            "connected" => $"Server '{evt.ServerName}' connected ({evt.ToolCount} tools).",
            "connect_failed" => $"Server '{evt.ServerName}' failed to connect: {evt.Detail ?? "connection failed"}.",
            "disconnected" => $"Server '{evt.ServerName}' disconnected.",
            "disconnect_failed" => $"Server '{evt.ServerName}' failed to disconnect: {evt.Detail ?? "unknown error"}.",
            _ => evt.Detail ?? $"MCP event '{evt.EventType}' for server '{evt.ServerName}'."
        };

        return graph.LogActionAsync(new AgentAction
        {
            ActionType = "mcp",
            Detail = detail,
            Timestamp = DateTime.UtcNow
        }, ct);
    }
}
