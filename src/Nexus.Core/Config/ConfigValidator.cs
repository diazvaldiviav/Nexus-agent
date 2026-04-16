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
