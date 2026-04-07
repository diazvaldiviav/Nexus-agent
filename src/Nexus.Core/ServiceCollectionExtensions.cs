using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.Core.Abstractions;
using Nexus.Core.Config;
using Nexus.Core.Models;
using Nexus.Core.Providers;
using Nexus.Core.Services;
using Nexus.Memory.Abstractions;
using Nexus.Memory.Embedding;
using Nexus.Memory.Graph;
using Nexus.Memory.Infrastructure;
using Nexus.Memory.Processing;

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

        var knowledgeGraph = new KnowledgeGraph(dbInit.ConnectionString);
        services.AddSingleton<IKnowledgeGraph>(knowledgeGraph);
        services.AddSingleton<IActionLogNotifier>(knowledgeGraph);
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

            // Resolve cloud fallback: Gemini (from models.gemini) > OpenAI (from embeddings.api_key / env)
            var googleApiKey = config.Models.GetApiKey("gemini");

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
            sp.GetRequiredService<IKnowledgeGraph>(),
            sp.GetRequiredService<SemanticSearch>(),
            sp.GetService<IEmbeddingService>(),
            config.Memory.WorkingMemoryMaxTokens,
            config.Memory.RelevantMemoryMaxTokens,
            config.Memory.MaxRetrievalNodes,
            sp.GetService<ILogger<MemoryContextBuilder>>(),
            config.Memory.RecentInteractionsFetchLimit));
        services.AddSingleton(sp => new PromptBuilder(
            sp.GetRequiredService<MemoryContextBuilder>(),
            config.Agent,
            sp.GetService<IToolExecutor>()));

        // Registration order: ModelRouter -> ILlmClient -> EntityExtractor -> Providers -> Factory -> AgentService
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
            var graph = sp.GetRequiredService<IKnowledgeGraph>();
            var llmClient = sp.GetService<ILlmClient>();
            var embeddingService = sp.GetService<IEmbeddingService>();

            // Gemini fallback: resolve API key from config or environment
            var geminiApiKey = config.Models.GetApiKey("gemini");
            HttpClient? geminiHttp = null;
            if (!string.IsNullOrEmpty(geminiApiKey))
            {
                // Singleton lifetime — allocated once, lives for app duration
                geminiHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            }

            return new EntityExtractor(graph, llmClient, embeddingService, geminiHttp, geminiApiKey,
                sp.GetService<ILogger<EntityExtractor>>());
        });

        // LLM Providers (multi-registration for LlmProviderFactory)
        services.AddSingleton<ILlmProvider>(sp =>
            new OllamaLlmProvider(config.Models.Local));

        var geminiProviderApiKey = config.Models.GetApiKey("gemini");

        if (!string.IsNullOrEmpty(geminiProviderApiKey))
        {
            services.AddSingleton<ILlmProvider>(sp =>
                new GeminiLlmProvider(geminiProviderApiKey, endpoint: config.Models.GetEndpoint("gemini"),
                    logger: sp.GetService<ILogger<GeminiLlmProvider>>()));
        }

        // Anthropic provider
        var anthropicApiKey = config.Models.GetApiKey("anthropic");

        if (!string.IsNullOrEmpty(anthropicApiKey))
        {
            services.AddSingleton<ILlmProvider>(sp =>
                new AnthropicLlmProvider(anthropicApiKey,
                    endpoint: config.Models.GetEndpoint("anthropic"),
                    logger: sp.GetService<ILogger<AnthropicLlmProvider>>()));
        }

        // OpenAI provider
        var openAiProviderApiKey = config.Models.GetApiKey("openai");

        if (!string.IsNullOrEmpty(openAiProviderApiKey))
        {
            services.AddSingleton<ILlmProvider>(sp =>
                new OpenAiLlmProvider(openAiProviderApiKey,
                    endpoint: config.Models.GetEndpoint("openai"),
                    logger: sp.GetService<ILogger<OpenAiLlmProvider>>()));
        }

        services.AddSingleton(sp => new EntityResolver(
            sp.GetRequiredService<IKnowledgeGraph>(),
            sp.GetService<IEmbeddingService>(),
            sp.GetService<ILlmClient>(),
            config.Memory.DeduplicationThreshold,
            sp.GetService<ILogger<EntityResolver>>()));

        services.AddSingleton(sp => new MemoryCompressor(
            sp.GetRequiredService<IKnowledgeGraph>(),
            ConfigLoader.GetArchivePath(config),
            config.Memory.ArchiveThresholdDays,
            sp.GetService<ILlmClient>(),
            sp.GetService<IEmbeddingService>(),
            sp.GetService<ILogger<MemoryCompressor>>()));

        services.AddSingleton<IInteractionSummarizer>(sp => new InteractionSummarizer(
            sp.GetRequiredService<IKnowledgeGraph>(),
            sp.GetService<ILlmClient>(),
            sp.GetService<IEmbeddingService>(),
            sp.GetService<ILogger<InteractionSummarizer>>()));

        services.AddSingleton<LlmProviderFactory>();

        services.AddSingleton(sp => new ContextWindowManager(
            sp.GetRequiredService<IInteractionSummarizer>(),
            sp.GetRequiredService<PromptBuilder>(),
            config.Memory,
            sp.GetService<ILogger<ContextWindowManager>>()));

        services.AddSingleton(sp => new AgentService(
            config,
            sp.GetRequiredService<IKnowledgeGraph>(),
            sp.GetRequiredService<PromptBuilder>(),
            sp.GetRequiredService<ModelRouter>(),
            sp.GetRequiredService<EntityExtractor>(),
            sp.GetRequiredService<LlmProviderFactory>(),
            sp.GetRequiredService<IInteractionSummarizer>(),
            sp.GetService<IToolExecutor>(),
            sp.GetService<IToolArgumentValidator>(),
            sp.GetService<EntityResolver>(),
            sp.GetService<MemoryCompressor>(),
            sp.GetService<ContextWindowManager>(),
            sp.GetService<ILogger<AgentService>>()));
        services.AddSingleton<IAgentService>(sp => sp.GetRequiredService<AgentService>());
        services.AddSingleton(sp => new RelevanceDecay(
            dbInit.ConnectionString,
            config.Memory.RelevanceDecayLambda,
            config.Memory.WorkingThresholdScore,
            config.Memory.WorkingThresholdMentions,
            config.Memory.ArchiveThresholdScore,
            sp.GetService<MemoryCompressor>()));

        return services;
    }
}
