using Nexus.Core.Models;

namespace Nexus.Core.Abstractions;

/// <summary>
/// Abstraction for LLM chat providers (local and cloud).
/// Each provider handles its own HTTP API format and streaming protocol.
/// </summary>
public interface ILlmProvider
{
    /// <summary>
    /// Provider identifier used by LlmProviderFactory for lookup (e.g., "ollama", "gemini").
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Sends a chat request and returns the complete response.
    /// conversationHistory includes the current user turn as the last entry.
    /// </summary>
    Task<string> ChatAsync(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> conversationHistory,
        string model,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a chat request and streams response tokens as they arrive.
    /// </summary>
    IAsyncEnumerable<string> ChatStreamAsync(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> conversationHistory,
        string model,
        CancellationToken cancellationToken = default);
}
