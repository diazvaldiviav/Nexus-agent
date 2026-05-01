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
    public PermissionConfig Permission { get; set; } = new();
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
    public int ToolPlanningTimeoutSeconds { get; set; } = 30;
    public bool SchemaValidationEnabled { get; set; } = true;
    public bool TypeCoercionEnabled { get; set; } = true;
    public int MaxOutputLines { get; set; } = 200;
    public int MaxOutputBytes { get; set; } = 32000;
    public bool ToolFilteringEnabled { get; set; } = false;
    public bool ToolPlanningEnabled { get; set; } = false;
    public int StepExecutionMaxAttempts { get; set; } = 5;

    /// <summary>
    /// When true, <c>ToolPlanner</c> falls through to embedding-based semantic matching
    /// for plan steps that the lexical 3-tier matcher could not resolve. Default true.
    /// Requires an <c>IEmbeddingService</c> registered in DI; absent service → no-op.
    /// Layer 2 of the Sprint 10 follow-up plan-execution robustness defense.
    /// </summary>
    public bool ToolPlannerEmbeddingFallbackEnabled { get; set; } = true;

    /// <summary>
    /// Minimum cosine similarity (0.0-1.0) required for the embedding fallback to
    /// accept a tool match. Default 0.65. Range 0.40-0.95, validator-enforced.
    /// </summary>
    public float ToolPlannerEmbeddingMatchThreshold { get; set; } = 0.65f;

    // Phase 9 — Planner Context Builder

    /// <summary>
    /// When true, <c>PlannerContextBuilder</c> injects a compacted recent-turn summary into the planner prompt. Defaults true.
    /// </summary>
    public bool PlannerContextEnabled { get; set; } = true;

    /// <summary>Total UTF-8 byte cap for the planner context block. Range 200-16000.</summary>
    public int PlannerContextMaxBytes { get; set; } = 1500;

    /// <summary>Maximum non-synthetic turns retained. Range 1-20.</summary>
    public int PlannerContextMaxRecentTurns { get; set; } = 4;

    /// <summary>Per-turn UTF-8 byte cap before ellipsis-truncation. Range 80-4000.</summary>
    public int PlannerContextMaxBytesPerTurn { get; set; } = 280;

    // Phase 9 — Tool Verification

    /// <summary>
    /// When true, mutating MCP tool calls run through <c>IToolVerifier</c>. Failure decorates the result with <c>[VerificationWarning]</c>
    /// and triggers retry within <c>StepExecutionMaxAttempts</c>. Defaults true.
    /// </summary>
    public bool ToolVerificationEnabled { get; set; } = true;

    /// <summary>Hard cap on per-snapshot read invocation. Range 1-60.</summary>
    public int VerificationSnapshotTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Minimum fuzzy-similarity score (0-100) required before PathValidator silently substitutes
    /// a fuzzy match for a missing path. Default 80. Range 50-100.
    /// Empirically calibrated: Bug 4 (silent stale-state corruption) scores 60-70 on full-path
    /// Fuzz.Ratio, while legitimate typo+relative-path corrections score 80-95. Threshold 80
    /// separates them cleanly. The basename-uniqueness short-circuit (in FindBestMatchWithScore)
    /// further relaxes this for unambiguous matches (single exact or single fuzzy candidate).
    /// </summary>
    public int PathValidatorStrictDistance { get; set; } = 80;

    // AC-1 — Planner Heuristic Gate

    /// <summary>
    /// When true, <c>PlannerInvocationHeuristic</c> runs before the planner LLM call.
    /// Short messages and chat greetings bypass the planner entirely. Defaults true.
    /// </summary>
    public bool PlannerHeuristicEnabled { get; set; } = true;

    /// <summary>
    /// Minimum character length a message must reach before it is eligible for planning.
    /// Messages shorter than this threshold are skipped with reason "below_min_length".
    /// Range 1-200. Default 16.
    /// </summary>
    public int PlannerHeuristicMinLength { get; set; } = 16;

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

/// <summary>
/// Controls the permission gate that guards destructive and sensitive tool invocations.
/// </summary>
public sealed class PermissionConfig
{
    /// <summary>When false, the permission gate is bypassed entirely (all tools auto-allowed). Default true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Per-tool overrides keyed by tool name. Each entry can declare a default <see cref="PermissionToolRule.Action"/>
    /// and optional per-glob-pattern overrides.
    /// </summary>
    public Dictionary<string, PermissionToolRule> Tools { get; set; } = new();
}

/// <summary>
/// Permission policy for a single tool.
/// </summary>
public sealed class PermissionToolRule
{
    /// <summary>Default action for the tool: <c>"allow"</c>, <c>"ask"</c>, or <c>"deny"</c>. Null means defer to gate logic.</summary>
    public string? Action { get; set; }

    /// <summary>
    /// Per-glob-pattern overrides. Keys are glob patterns (e.g. <c>"**/*.env"</c>); values are actions
    /// (<c>"allow"</c>, <c>"ask"</c>, <c>"deny"</c>). First matching pattern wins.
    /// </summary>
    public Dictionary<string, string>? Patterns { get; set; }
}
