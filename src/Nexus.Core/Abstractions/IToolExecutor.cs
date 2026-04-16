namespace Nexus.Core.Abstractions;

/// <summary>
/// Abstraction for executing tools discovered from MCP servers.
/// Implemented by McpToolExecutor in Nexus.Connectors.
/// </summary>
public interface IToolExecutor
{
    /// <summary>
    /// Returns a formatted string of all available tool definitions suitable for inclusion in an LLM prompt.
    /// </summary>
    string GetToolDefinitionsForPrompt();

    /// <summary>
    /// Returns tool definitions filtered/annotated for the given model's capability tier.
    /// Default implementation ignores modelName and falls back to the unfiltered version.
    /// </summary>
    /// <param name="modelName">The model name used to determine tool capability tier for filtering.</param>
    string GetToolDefinitionsForPrompt(string? modelName) => GetToolDefinitionsForPrompt();

    /// <summary>
    /// Invokes a tool on the specified MCP server with the given parameters.
    /// </summary>
    Task<string> InvokeToolAsync(
        string serverName,
        string toolName,
        Dictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Indicates whether any tools are currently registered.
    /// </summary>
    bool HasTools { get; }
}
