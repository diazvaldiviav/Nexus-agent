using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.Connectors.Catalog;
using Nexus.Connectors.ToolFiltering;
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

        services.AddSingleton<IToolComplexityClassifier>(sp =>
            new ToolComplexityClassifier(sp.GetService<ILogger<ToolComplexityClassifier>>()));
        services.AddSingleton(sp =>
            new ToolPromptFormatter(
                sp.GetRequiredService<IToolComplexityClassifier>(),
                sp.GetService<ILogger<ToolPromptFormatter>>()));

        services.AddSingleton<IToolExecutor>(sp =>
            new McpToolExecutor(
                sp.GetRequiredService<IMcpClientManager>(),
                sp.GetRequiredService<ToolRegistry>(),
                sp.GetService<ILogger<McpToolExecutor>>(),
                sp.GetRequiredService<ToolPromptFormatter>(),
                sp.GetRequiredService<NexusConfig>().Mcp.ToolFilteringEnabled));

        services.AddSingleton<IToolArgumentValidator>(sp =>
            new PathValidator(
                sp.GetRequiredService<NexusConfig>(),
                sp.GetRequiredService<ToolRegistry>(),
                sp.GetService<ILogger<PathValidator>>()));

        // IVerificationCatalog must be registered BEFORE IToolVerifier (verifier depends on catalog).
        services.AddSingleton<IVerificationCatalog>(sp =>
            new VerificationCatalog(
                sp.GetRequiredService<NexusConfig>(),
                sp.GetService<ILogger<VerificationCatalog>>()));

        services.AddSingleton<IToolVerifier>(sp =>
            new McpToolVerifier(
                sp.GetRequiredService<IVerificationCatalog>(),
                sp.GetRequiredService<IMcpClientManager>(),
                sp.GetRequiredService<NexusConfig>(),
                sp.GetService<ILogger<McpToolVerifier>>()));

        services.AddSingleton<ISchemaValidator>(sp =>
            new SchemaValidator(
                sp.GetRequiredService<ToolRegistry>(),
                sp.GetRequiredService<NexusConfig>().Mcp.TypeCoercionEnabled,
                sp.GetService<ILogger<SchemaValidator>>()));

        return services;
    }
}
