using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.Core.Abstractions;
using Nexus.Core.Config;

namespace Nexus.Connectors;

/// <summary>
/// DI registration for MCP (Model Context Protocol) services.
/// Call services.AddNexusMcp() to register McpClientManager, ToolRegistry, IToolExecutor, and IToolArgumentValidator.
/// </summary>
public static class McpServiceCollectionExtensions
{
    public static IServiceCollection AddNexusMcp(this IServiceCollection services)
    {
        services.AddSingleton<McpClientManager>(sp =>
            new McpClientManager(sp.GetService<ILogger<McpClientManager>>()));
        services.AddSingleton<IMcpClientManager>(sp => sp.GetRequiredService<McpClientManager>());

        services.AddSingleton<ToolRegistry>(sp =>
            new ToolRegistry(sp.GetService<ILogger<ToolRegistry>>()));
        services.AddSingleton<IToolRegistry>(sp => sp.GetRequiredService<ToolRegistry>());

        services.AddSingleton<McpLifecycleService>();

        services.AddSingleton<IToolExecutor>(sp =>
            new McpToolExecutor(
                sp.GetRequiredService<McpClientManager>(),
                sp.GetRequiredService<ToolRegistry>(),
                sp.GetService<ILogger<McpToolExecutor>>()));

        services.AddSingleton<IToolArgumentValidator>(sp =>
            new PathValidator(
                sp.GetRequiredService<NexusConfig>(),
                sp.GetRequiredService<ToolRegistry>(),
                sp.GetService<ILogger<PathValidator>>()));

        return services;
    }
}
