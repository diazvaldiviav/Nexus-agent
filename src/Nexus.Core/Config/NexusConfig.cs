using YamlDotNet.Serialization;

namespace Nexus.Core.Config;

public class NexusConfig
{
    public AgentConfig Agent { get; set; } = new();
    public ModelsConfig Models { get; set; } = new();
    public EmbeddingsConfig Embeddings { get; set; } = new();
    public MemoryConfig Memory { get; set; } = new();
    public McpConfig Mcp { get; set; } = new();
    public UiConfig Ui { get; set; } = new();
}

public class AgentConfig
{
    public string Name { get; set; } = "Nexus";
    public string Language { get; set; } = "en";
}

public class ProviderKeyConfig
{
    public string? ApiKey { get; set; }
    public string? Endpoint { get; set; }
}

public class ModelsConfig
{
    public ModelProviderConfig Local { get; set; } = new();
    public ModelProviderConfig Cloud { get; set; } = new();
    public RoutingConfig Routing { get; set; } = new();

    public ProviderKeyConfig? Gemini { get; set; }
    public ProviderKeyConfig? Anthropic { get; set; }

    [YamlMember(Alias = "openai")]
    public ProviderKeyConfig? OpenAi { get; set; }

    public string? GetApiKey(string providerName)
    {
        var (section, envVars) = ResolveProvider(providerName);

        if (!string.IsNullOrEmpty(section?.ApiKey))
            return section.ApiKey;

        if (string.Equals(Cloud.Provider, providerName, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(Cloud.ApiKey))
            return Cloud.ApiKey;

        foreach (var envVar in envVars)
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrEmpty(value))
                return value;
        }

        return null;
    }

    public string? GetEndpoint(string providerName)
    {
        var (section, _) = ResolveProvider(providerName);

        if (!string.IsNullOrEmpty(section?.Endpoint))
            return section.Endpoint;

        if (string.Equals(Cloud.Provider, providerName, StringComparison.OrdinalIgnoreCase))
            return Cloud.Endpoint;

        return null;
    }

    private (ProviderKeyConfig? section, string[] envVars) ResolveProvider(string providerName)
    {
        return providerName.ToLowerInvariant() switch
        {
            "gemini" or "google" => (Gemini, new[] { "GEMINI_API_KEY", "GOOGLE_API_KEY" }),
            "anthropic"          => (Anthropic, new[] { "ANTHROPIC_API_KEY" }),
            "openai"             => (OpenAi, new[] { "OPENAI_API_KEY" }),
            _                    => (null, Array.Empty<string>()),
        };
    }
}

public class ModelProviderConfig
{
    public string Provider { get; set; } = "ollama";
    public string Model { get; set; } = "qwen3:14b";
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public int ContextWindow { get; set; } = 8192;
    public int MaxOutputTokens { get; set; } = 2048;
}

public class RoutingConfig
{
    public string EntityExtraction { get; set; } = "local";
    public string InteractionSummary { get; set; } = "local";
    public string EntityResolution { get; set; } = "local";
    public string MemoryQueryResponse { get; set; } = "local";
    public string ComplexReasoning { get; set; } = "cloud";
    public string CodeGeneration { get; set; } = "cloud";
    public string Default { get; set; } = "local";
}

public class EmbeddingsConfig
{
    public string Provider { get; set; } = "ollama";
    public string Model { get; set; } = "nomic-embed-text";
    public string? Endpoint { get; set; }
    public int Dimensions { get; set; } = 768;
    public string? ApiKey { get; set; }
}

public class MemoryConfig
{
    public string Database { get; set; } = "~/.nexus/memory.db";
    public int WorkingMemoryMaxTokens { get; set; } = 1000;
    public int RelevantMemoryMaxTokens { get; set; } = 3000;
    public int MaxRetrievalNodes { get; set; } = 20;
    public double RelevanceDecayLambda { get; set; } = 0.05;
    public double WorkingThresholdScore { get; set; } = 0.7;
    public int WorkingThresholdMentions { get; set; } = 3;
    public double ArchiveThresholdScore { get; set; } = 0.05;
    public int ArchiveThresholdDays { get; set; } = 90;
    public int SummarizationInterval { get; set; } = 10;
    public int RecentInteractionsFetchLimit { get; set; } = 5;
    public double DeduplicationThreshold { get; set; } = 0.85;
    public string ArchivePath { get; set; } = "~/.nexus/archive/";
    public bool CompressionEnabled { get; set; } = true;
    public double ContextCompactionThreshold { get; set; } = 0.70;
    public int CompactionKeepRecentMessages { get; set; } = 4;
}

public class McpConfig
{
    public int MaxToolCallIterations { get; set; } = 3;
    public int ToolCallTimeoutSeconds { get; set; } = 30;
    public bool SchemaValidationEnabled { get; set; } = true;
    public bool TypeCoercionEnabled { get; set; } = true;
    public int MaxOutputLines { get; set; } = 200;
    public int MaxOutputBytes { get; set; } = 32000;
    public List<McpServerEntry> Servers { get; set; } = new();
}

/// <summary>
/// Configuration for a single MCP server connection.
/// Supports stdio (primary) and sse transports.
/// </summary>
public class McpServerEntry
{
    public string Name { get; set; } = string.Empty;
    public string Transport { get; set; } = "stdio";
    public string? Command { get; set; }
    public List<string> Args { get; set; } = new();
    public string? Url { get; set; }
    public Dictionary<string, string> Env { get; set; } = new();
}

public class UiConfig
{
    public string Theme { get; set; } = "fluent";
    public string GraphLayout { get; set; } = "force-directed";
    public string DefaultView { get; set; } = "chat";
}
