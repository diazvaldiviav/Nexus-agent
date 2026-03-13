namespace Nexus.Memory;

/// <summary>
/// Abstraction for invoking an LLM with a prompt. Lives in Nexus.Memory
/// to avoid circular dependency with Nexus.Core.
/// </summary>
public interface ILlmClient
{
    /// <summary>
    /// Sends a prompt to the LLM and returns the raw text response.
    /// </summary>
    /// <param name="prompt">The complete prompt text to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The LLM's text response.</returns>
    Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default);
}
