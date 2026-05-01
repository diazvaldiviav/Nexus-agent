using Nexus.Core.Models;

namespace Nexus.Core.Abstractions;

/// <summary>
/// Builds a compact <see cref="PlannerContext"/> from recent conversation history
/// using a fast, deterministic heuristic — no LLM call, no I/O.
/// </summary>
public interface IPlannerContextBuilder
{
    /// <summary>
    /// Compacts <paramref name="conversationHistory"/> into a <see cref="PlannerContext"/>
    /// the planner can inject into its prompt.
    /// </summary>
    /// <param name="conversationHistory">Full conversation history at the time of planning.</param>
    /// <param name="userMessage">The current user message (not yet appended to history).</param>
    /// <param name="cancellationToken">Cancellation token honored before the heuristic begins.</param>
    /// <returns>
    /// A <see cref="PlannerContext"/> with recent turns and a summary, or
    /// <see cref="PlannerContext.Empty"/> when there is no useful context.
    /// </returns>
    Task<PlannerContext> BuildAsync(
        IReadOnlyList<ConversationMessage> conversationHistory,
        string userMessage,
        CancellationToken cancellationToken = default);
}
