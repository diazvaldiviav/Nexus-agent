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

        var defaultEndpoint = string.Equals(config.Embeddings.Provider, "openai", StringComparison.OrdinalIgnoreCase)
            ? "https://api.openai.com"
            : "http://localhost:11434";
        services.AddSingleton(new EmbeddingOptions(
            config.Embeddings.Endpoint ?? defaultEndpoint,
            config.Embeddings.Model,
            config.Embeddings.Dimensions));
        services.AddSingleton<IEmbeddingService>(sp =>
        {
            var options = sp.GetRequiredService<EmbeddingOptions>();
            var logger = sp.GetService<ILogger<FallbackEmbeddingService>>();

            if (string.Equals(config.Embeddings.Provider, "openai", StringComparison.OrdinalIgnoreCase))
            {
                var openAiApiKey = config.Embeddings.ApiKey
                                ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                                ?? throw new InvalidOperationException(
                                     "OpenAI API key is required for embeddings. " +
                                     "Set it in nexus.yaml (embeddings.api_key) or via OPENAI_API_KEY environment variable.");
                return new OpenAiEmbeddingService(options, openAiApiKey);
            }

            // Default: Ollama primary, cloud fallback if API key available
            var primary = new OllamaEmbeddingService(options);

            // Resolve cloud fallback: Gemini (from models.cloud) > OpenAI (from embeddings.api_key / env)
            var googleApiKey = config.Models.Cloud.ApiKey
                            ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY")
                            ?? Environment.GetEnvironmentVariable("GOOGLE_API_KEY");

            if (!string.IsNullOrEmpty(googleApiKey))
            {
                var fallback = new GeminiEmbeddingService(googleApiKey);
                return new FallbackEmbeddingService(primary, fallback, logger);
            }

            var openAiKey = config.Embeddings.ApiKey
                         ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");

            if (!string.IsNullOrEmpty(openAiKey))
            {
                var cloudOptions = new EmbeddingOptions("https://api.openai.com", "text-embedding-3-small", 768);
                var fallback = new OpenAiEmbeddingService(cloudOptions, openAiKey);
                return new FallbackEmbeddingService(primary, fallback, logger);
            }

            return primary;
        });

        services.AddSingleton(sp => new MemoryContextBuilder(
            sp.GetRequiredService<KnowledgeGraph>(),
            sp.GetRequiredService<SemanticSearch>(),
            sp.GetService<IEmbeddingService>(),
            config.Memory.WorkingMemoryMaxTokens,
            config.Memory.RelevantMemoryMaxTokens,
            config.Memory.MaxRetrievalNodes,
            sp.GetService<ILogger<MemoryContextBuilder>>()));
        services.AddSingleton(sp => new PromptBuilder(
            sp.GetRequiredService<MemoryContextBuilder>(),
            config.Agent));

        // Registration order: ModelRouter -> ILlmClient -> EntityExtractor (dependency chain)
        services.AddSingleton(sp => new ModelRouter(
            config.Models.Routing,
            sp.GetService<ILogger<ModelRouter>>()));
        services.AddSingleton<ILlmClient>(sp =>
        {
            var router = sp.GetRequiredService<ModelRouter>();
            var modelConfig = router.IsLocal(TaskType.EntityExtraction)
                ? config.Models.Local
                : config.Models.Cloud;
            return new OllamaLlmClient(modelConfig);
        });
        services.AddSingleton(sp =>
        {
            var graph = sp.GetRequiredService<KnowledgeGraph>();
            var llmClient = sp.GetService<ILlmClient>();
            var embeddingService = sp.GetService<IEmbeddingService>();

            // Gemini fallback: resolve API key from config or environment
            string? geminiApiKey = null;
            HttpClient? geminiHttp = null;
            if (!string.IsNullOrEmpty(config.Models.Cloud.ApiKey))
            {
                geminiApiKey = config.Models.Cloud.ApiKey;
            }
            else
            {
                geminiApiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
                            ?? Environment.GetEnvironmentVariable("GOOGLE_API_KEY");
            }
            if (!string.IsNullOrEmpty(geminiApiKey))
            {
                // Singleton lifetime — allocated once, lives for app duration
                geminiHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            }

            return new EntityExtractor(graph, llmClient, embeddingService, geminiHttp, geminiApiKey,
                sp.GetService<ILogger<EntityExtractor>>());
        });
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
