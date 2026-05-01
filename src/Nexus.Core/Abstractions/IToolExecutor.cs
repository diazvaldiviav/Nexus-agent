using System.Text.Json;

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
    /// Returns the raw JSON InputSchema for the named tool, or null if the tool is not
    /// registered or has no schema. Used by the plan executor to build fill-in-the-blanks
    /// tool-call templates for small models.
    /// </summary>
    JsonElement? GetToolSchema(string toolName) => null;

    /// <summary>
    /// Returns the registered tool descriptor for <paramref name="toolName"/>, or null if not registered.
    /// Default implementation returns null (consumers that need server routing must override).
    /// Return type is <see cref="object"/> to keep Core layer free of MCP/Connectors types — callers
    /// downcast as needed (McpToolVerifier casts to <c>Nexus.Connectors.ToolDefinition</c>).
    /// </summary>
    /// <remarks>
    /// Returns object? to keep Nexus.Core free of Nexus.Connectors.ToolDefinition.
    /// The single consumer (McpToolVerifier in Connectors) downcasts.
    /// </remarks>
    object? GetToolDefinition(string toolName) => null;

    /// <summary>
    /// Returns the MCP server name that hosts the given tool, or an empty string if unknown.
    /// Default implementation returns empty string. Mirrors the GetToolSchema default-method pattern.
    /// Used by AgentService to pass the server name to IToolVerifier without taking a dependency
    /// on Nexus.Connectors.ToolDefinition.
    /// </summary>
    string GetToolServerName(string toolName) => string.Empty;

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
