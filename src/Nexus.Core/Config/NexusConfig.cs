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

public class ModelsConfig
{
    public ModelProviderConfig Local { get; set; } = new();
    public ModelProviderConfig Cloud { get; set; } = new();
    public RoutingConfig Routing { get; set; } = new();
}

public class ModelProviderConfig
{
    public string Provider { get; set; } = "ollama";
    public string Model { get; set; } = "qwen3:14b";
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
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
}

public class McpConfig
{
    public List<McpServerConfig> Servers { get; set; } = new();
}

public class McpServerConfig
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public class UiConfig
{
    public string Theme { get; set; } = "fluent";
    public string GraphLayout { get; set; } = "force-directed";
    public string DefaultView { get; set; } = "chat";
}
