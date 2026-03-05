using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.Core.Config;
using Nexus.Memory;

namespace Nexus.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNexusAgent(this IServiceCollection services, NexusConfig? config = null)
    {
        config ??= ConfigLoader.Load();

        var dbPath = ConfigLoader.GetDatabasePath(config);
        var dbInit = new DatabaseInitializer(dbPath);
        dbInit.Initialize();

        services.AddSingleton(config);
        services.AddSingleton(config.Agent);
        services.AddSingleton(config.Models.Routing);

        services.AddSingleton(_ => new KnowledgeGraph(dbInit.ConnectionString));
        services.AddSingleton(_ => new SemanticSearch(dbInit.ConnectionString));
        services.AddSingleton(sp => new MemoryContextBuilder(
            sp.GetRequiredService<KnowledgeGraph>(),
            sp.GetRequiredService<SemanticSearch>(),
            config.Memory.WorkingMemoryMaxTokens,
            config.Memory.RelevantMemoryMaxTokens,
            config.Memory.MaxRetrievalNodes));
        services.AddSingleton(sp => new EntityExtractor(sp.GetRequiredService<KnowledgeGraph>()));
        services.AddSingleton(sp => new PromptBuilder(
            sp.GetRequiredService<MemoryContextBuilder>(),
            config.Agent));
        services.AddSingleton(sp => new ModelRouter(
            config.Models.Routing,
            sp.GetService<ILogger<ModelRouter>>()));
        services.AddSingleton(sp => new AgentService(
            config,
            sp.GetRequiredService<KnowledgeGraph>(),
            sp.GetRequiredService<PromptBuilder>(),
            sp.GetRequiredService<ModelRouter>(),
            sp.GetRequiredService<EntityExtractor>(),
            sp.GetService<ILogger<AgentService>>()));
        services.AddSingleton(_ => new RelevanceDecay(
            dbInit.ConnectionString,
            config.Memory.RelevanceDecayLambda,
            config.Memory.WorkingThresholdScore,
            config.Memory.WorkingThresholdMentions,
            config.Memory.ArchiveThresholdScore));

        return services;
    }
}
