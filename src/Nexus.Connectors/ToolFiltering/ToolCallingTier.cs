namespace Nexus.Connectors.ToolFiltering;

/// <summary>
/// Model capability tier for tool calling, derived from parameter count.
/// Order matters: lower-capability tiers come first.
/// </summary>
public enum ToolCallingTier
{
    /// <summary>
    /// Models below 4B parameters (e.g., qwen3:1.7b, gemma2:2b, llama3.2:3b).
    /// Cannot reliably emit valid <c>[TOOL_CALL: {...}]</c> JSON — the planner is
    /// skipped entirely and the agent falls through to a chat-only loop with no tools.
    /// In non-interactive permission contexts, ChatOnly is treated like Limited (auto-deny).
    /// </summary>
    ChatOnly,

    /// <summary>
    /// Models 4B–7.9B parameters (e.g., Qwen3.5:4B, mistral:7b).
    /// Tool calls work but Complex schemas are excluded; <c>edit_file</c> is replaced
    /// by the workflow override <c>read_text_file → modify content → write_file</c>.
    /// </summary>
    Limited,

    /// <summary>
    /// Models 8B–29.9B parameters (e.g., qwen3:8b, qwen3:14b, qwen3:22b).
    /// All tools included with complexity hints; no workflow overrides.
    /// </summary>
    Capable,

    /// <summary>
    /// Models 30B+ or unknown (e.g., qwen3:32b, llama3:70b).
    /// No filters or hints; permissions auto-approve in non-interactive mode.
    /// </summary>
    Full
}
