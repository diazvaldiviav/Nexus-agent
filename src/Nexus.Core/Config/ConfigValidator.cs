namespace Nexus.Core.Config;

public record ValidationResult(Dictionary<string, string> Errors)
{
    public bool IsValid => Errors.Count == 0;
    public string? GetError(string field) => Errors.TryGetValue(field, out var msg) ? msg : null;
}

public static class ConfigValidator
{
    public static ValidationResult Validate(NexusConfig config)
    {
        var errors = new Dictionary<string, string>();
        AddIfNotNull(errors, "DecayLambda", ValidateDecayLambda(config.Memory.RelevanceDecayLambda));
        AddIfNotNull(errors, "LocalEndpoint", ValidateLocalEndpoint(config.Models.Local.Endpoint));
        AddIfNotNull(errors, "SummarizationInterval", ValidateSummarizationInterval(config.Memory.SummarizationInterval));
        AddIfNotNull(errors, "RecentInteractionsFetchLimit", ValidateRecentInteractionsFetchLimit(config.Memory.RecentInteractionsFetchLimit));
        AddIfNotNull(errors, "Mcp.MaxToolCallIterations", ValidateMaxToolCallIterations(config.Mcp.MaxToolCallIterations));
        AddIfNotNull(errors, "Mcp.ToolCallTimeoutSeconds", ValidateToolCallTimeoutSeconds(config.Mcp.ToolCallTimeoutSeconds));
        AddIfNotNull(errors, "Mcp.MaxOutputLines", ValidateMaxOutputLines(config.Mcp.MaxOutputLines));
        AddIfNotNull(errors, "Mcp.MaxOutputBytes", ValidateMaxOutputBytes(config.Mcp.MaxOutputBytes));
        AddIfNotNull(errors, "Mcp.ToolFilteringEnabled",
            ValidateToolFilteringEnabled(config.Mcp.ToolFilteringEnabled, config.Models.Local.Model));
        AddIfNotNull(errors, "Mcp.ToolPlanningEnabled",
            ValidateToolPlanningEnabled(config.Mcp.ToolPlanningEnabled, config.Models.Local));
        AddIfNotNull(errors, "Mcp.ToolPlanningTimeoutSeconds",
            ValidateToolPlanningTimeoutSeconds(config.Mcp.ToolPlanningTimeoutSeconds));
        AddIfNotNull(errors, "Mcp.StepExecutionMaxAttempts",
            ValidateStepExecutionMaxAttempts(config.Mcp.StepExecutionMaxAttempts));
        AddIfNotNull(errors, "Mcp.PlannerContextMaxBytes",
            ValidatePlannerContextMaxBytes(config.Mcp.PlannerContextMaxBytes));
        AddIfNotNull(errors, "Mcp.PlannerContextMaxRecentTurns",
            ValidatePlannerContextMaxRecentTurns(config.Mcp.PlannerContextMaxRecentTurns));
        AddIfNotNull(errors, "Mcp.PlannerContextMaxBytesPerTurn",
            ValidatePlannerContextMaxBytesPerTurn(config.Mcp.PlannerContextMaxBytesPerTurn));
        AddIfNotNull(errors, "Mcp.VerificationSnapshotTimeoutSeconds",
            ValidateVerificationSnapshotTimeoutSeconds(config.Mcp.VerificationSnapshotTimeoutSeconds));
        AddIfNotNull(errors, "Mcp.PathValidatorStrictDistance",
            ValidatePathValidatorStrictDistance(config.Mcp.PathValidatorStrictDistance));
        AddIfNotNull(errors, "Mcp.PlannerHeuristicMinLength",
            ValidatePlannerHeuristicMinLength(config.Mcp.PlannerHeuristicMinLength));
        AddIfNotNull(errors, "Mcp.OutputFidelityMinScore",
            ValidateOutputFidelityMinScore(config.Mcp.OutputFidelityMinScore));
        AddIfNotNull(errors, "Mcp.OutputFidelitySubstringWeight",
            ValidateOutputFidelityWeight(config.Mcp.OutputFidelitySubstringWeight, "SubstringWeight"));
        AddIfNotNull(errors, "Mcp.OutputFidelityEmbeddingWeight",
            ValidateOutputFidelityWeight(config.Mcp.OutputFidelityEmbeddingWeight, "EmbeddingWeight"));
        AddIfNotNull(errors, "Mcp.OutputFidelityWeights",
            ValidateOutputFidelityWeightSum(config.Mcp.OutputFidelitySubstringWeight, config.Mcp.OutputFidelityEmbeddingWeight));
        AddIfNotNull(errors, "Mcp.OutputFidelityMaxRetries",
            ValidateOutputFidelityMaxRetries(config.Mcp.OutputFidelityMaxRetries));
        AddIfNotNull(errors, "Mcp.ToolPlannerEmbeddingMatchThreshold",
            ValidateToolPlannerEmbeddingMatchThreshold(config.Mcp.ToolPlannerEmbeddingMatchThreshold));
        foreach (var (toolName, rule) in config.Permission.Tools)
        {
            AddIfNotNull(errors, $"Permission.Tools[{toolName}].Action",
                ValidatePermissionAction(rule.Action));
            if (rule.Patterns is not null)
                foreach (var (pattern, action) in rule.Patterns)
                    AddIfNotNull(errors, $"Permission.Tools[{toolName}].Patterns[{pattern}]",
                        ValidatePermissionAction(action));
        }
        for (var i = 0; i < config.Mcp.Servers.Count; i++)
            AddIfNotNull(errors, $"Mcp.Servers[{i}]", ValidateMcpServerEntry(config.Mcp.Servers[i]));
        return new ValidationResult(errors);
    }

    public static string? ValidateDecayLambda(double value)
        => value < 0.001 || value > 1.0 ? "Decay lambda must be between 0.001 and 1.0." : null;

    public static string? ValidateLocalEndpoint(string? value)
        => string.IsNullOrWhiteSpace(value) ? null
            : !Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")
            ? "Endpoint must be a valid HTTP or HTTPS URL." : null;

    public static string? ValidateSummarizationInterval(int value)
        => value < 1 ? "Summarization interval must be at least 1." : null;

    public static string? ValidateRecentInteractionsFetchLimit(int value)
        => value < 1 || value > 50 ? "Recent interactions limit must be between 1 and 50." : null;

    public static string? ValidateMaxToolCallIterations(int value)
        => value < 1 || value > 20 ? "MaxToolCallIterations must be between 1 and 20." : null;

    public static string? ValidateToolCallTimeoutSeconds(int value)
        => value < 1 || value > 300 ? "ToolCallTimeoutSeconds must be between 1 and 300." : null;

    public static string? ValidateMaxOutputLines(int value)
        => value < 1 || value > 10000 ? "MaxOutputLines must be between 1 and 10000." : null;

    public static string? ValidateMaxOutputBytes(int value)
        => value < 1000 || value > 500000 ? "MaxOutputBytes must be between 1000 and 500000." : null;

    public static string? ValidateToolFilteringEnabled(bool enabled, string? localModel)
        => enabled && string.IsNullOrWhiteSpace(localModel)
            ? "Tool filtering is enabled but no local model is configured."
            : null;

    public static string? ValidateToolPlanningEnabled(bool enabled, ModelProviderConfig local)
        => enabled && (string.IsNullOrWhiteSpace(local.Provider) || string.IsNullOrWhiteSpace(local.Model))
            ? "Tool planning is enabled but no local provider/model is configured."
            : null;

    public static string? ValidateToolPlanningTimeoutSeconds(int value)
        => value < 5 || value > 300 ? "ToolPlanningTimeoutSeconds must be between 5 and 300." : null;

    public static string? ValidateStepExecutionMaxAttempts(int value)
        => value < 1 || value > 20 ? "StepExecutionMaxAttempts must be between 1 and 20." : null;

    public static string? ValidatePlannerContextMaxBytes(int value)
        => value < 200 || value > 16000 ? "PlannerContextMaxBytes must be between 200 and 16000." : null;

    public static string? ValidatePlannerContextMaxRecentTurns(int value)
        => value < 1 || value > 20 ? "PlannerContextMaxRecentTurns must be between 1 and 20." : null;

    public static string? ValidatePlannerContextMaxBytesPerTurn(int value)
        => value < 80 || value > 4000 ? "PlannerContextMaxBytesPerTurn must be between 80 and 4000." : null;

    public static string? ValidateVerificationSnapshotTimeoutSeconds(int value)
        => value < 1 || value > 60 ? "VerificationSnapshotTimeoutSeconds must be between 1 and 60." : null;

    public static string? ValidatePathValidatorStrictDistance(int value)
        => value < 50 || value > 100 ? "PathValidatorStrictDistance must be between 50 and 100." : null;

    public static string? ValidatePlannerHeuristicMinLength(int value)
        => value < 1 || value > 200 ? "PlannerHeuristicMinLength must be between 1 and 200." : null;

    public static string? ValidateOutputFidelityMinScore(float value)
        => value < 0.0f || value > 0.95f
            ? "OutputFidelityMinScore must be between 0.0 and 0.95."
            : null;

    public static string? ValidateOutputFidelityWeight(float value, string fieldName)
        => value < 0.0f || value > 1.0f
            ? $"OutputFidelity{fieldName} must be between 0.0 and 1.0."
            : null;

    public static string? ValidateOutputFidelityWeightSum(float substring, float embedding)
    {
        var sum = substring + embedding;
        return Math.Abs(sum - 1.0f) > 0.01f
            ? $"OutputFidelity weights must sum to 1.0 ± 0.01 (got {sum:F2})."
            : null;
    }

    public static string? ValidateOutputFidelityMaxRetries(int value)
        => value < 0 || value > 3
            ? "OutputFidelityMaxRetries must be between 0 and 3."
            : null;

    public static string? ValidateToolPlannerEmbeddingMatchThreshold(float value)
        => value < 0.40f || value > 0.95f
            ? "ToolPlannerEmbeddingMatchThreshold must be between 0.40 and 0.95."
            : null;

    public static string? ValidatePermissionAction(string? action)
    {
        if (action is null) return null;
        return action.ToLowerInvariant() is "allow" or "ask" or "deny"
            ? null
            : $"PermissionToolRule.Action must be 'allow', 'ask', or 'deny' (got '{action}').";
    }

    public static string? ValidateMcpServerEntry(McpServerEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Name))
            return "Server name is required.";
        var transport = entry.Transport?.ToLowerInvariant();
        if (transport is not ("stdio" or "sse"))
            return $"Transport must be 'stdio' or 'sse', got '{entry.Transport}'.";
        if (transport == "stdio" && string.IsNullOrWhiteSpace(entry.Command))
            return $"Server '{entry.Name}': Command is required for stdio transport.";
        if (transport == "sse" && string.IsNullOrWhiteSpace(entry.Url))
            return $"Server '{entry.Name}': Url is required for sse transport.";
        if (transport == "sse" && !string.IsNullOrWhiteSpace(entry.Url)
            && (!Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri)
                || (uri.Scheme != "http" && uri.Scheme != "https")))
            return $"Server '{entry.Name}': Url must be a valid HTTP or HTTPS URL.";
        return null;
    }

    public static string? CheckApiKeyWarning(string? cloudProvider, string? geminiKey, string? anthropicKey, string? openAiKey)
    {
        return cloudProvider?.ToLowerInvariant() switch
        {
            "google" or "gemini" when string.IsNullOrWhiteSpace(geminiKey)
                => $"No API key configured for {cloudProvider}. Cloud features will not work.",
            "anthropic" when string.IsNullOrWhiteSpace(anthropicKey)
                => $"No API key configured for {cloudProvider}. Cloud features will not work.",
            "openai" when string.IsNullOrWhiteSpace(openAiKey)
                => $"No API key configured for {cloudProvider}. Cloud features will not work.",
            _ => null
        };
    }

    private static void AddIfNotNull(Dictionary<string, string> errors, string field, string? error)
    {
        if (error is not null) errors[field] = error;
    }
}
