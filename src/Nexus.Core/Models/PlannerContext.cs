using System.Text;

namespace Nexus.Core.Models;

/// <summary>
/// Compact representation of recent conversation context for the planner prompt.
/// Built heuristically — no LLM call, no I/O.
/// </summary>
public sealed record PlannerContext(
    string Summary,
    IReadOnlyList<string> RecentTurns,
    int TotalBytes)
{
    /// <summary>Sentinel returned when there is no useful context to provide.</summary>
    public static readonly PlannerContext Empty = new("", Array.Empty<string>(), 0);

    /// <summary>
    /// Returns <see langword="true"/> when both <see cref="Summary"/> and
    /// <see cref="RecentTurns"/> are empty (i.e., no context to inject).
    /// </summary>
    public bool IsEmpty => string.IsNullOrEmpty(Summary) && RecentTurns.Count == 0;

    /// <summary>
    /// Renders the context as a Markdown block suitable for inclusion in a planner prompt.
    /// Returns an empty string when <see cref="IsEmpty"/> is <see langword="true"/>.
    /// </summary>
    public string ToPromptBlock()
    {
        if (IsEmpty) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("## Conversation Context");
        if (!string.IsNullOrEmpty(Summary))
        {
            sb.Append("Working on: ").AppendLine(Summary);
        }
        if (RecentTurns.Count > 0)
        {
            sb.AppendLine("Recent turns:");
            foreach (var turn in RecentTurns)
            {
                sb.Append("- ").AppendLine(turn);
            }
        }
        return sb.ToString();
    }
}
