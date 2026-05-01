using Nexus.Core.Models;

namespace Nexus.Core.Services;

/// <summary>
/// Analyzes conversation history for failure sentinels and builds a grounding message
/// that prevents the final-summary LLM call from claiming success when steps failed.
/// </summary>
internal static class SummaryFailureAnalyzer
{
    /// <summary>
    /// Summary of failure indicators detected in conversation history.
    /// </summary>
    public sealed record Findings(
        int VerificationWarnings,
        int RetriesExhausted,
        int ToolErrors,
        int PermissionDenials,
        int DoomLoops,
        int StepsSkippedNoToolMatch,
        IReadOnlyList<string> ExcerptedReasons)
    {
        public bool HasFailures =>
            VerificationWarnings > 0 || RetriesExhausted > 0 || ToolErrors > 0 ||
            PermissionDenials > 0 || DoomLoops > 0 || StepsSkippedNoToolMatch > 0;
    }

    /// <summary>
    /// Performs a single pass over <paramref name="history"/> and counts all failure
    /// sentinel occurrences. Never throws.
    /// </summary>
    public static Findings Analyze(IReadOnlyList<ConversationMessage>? history)
    {
        if (history is null)
            return new Findings(0, 0, 0, 0, 0, 0, Array.Empty<string>());

        int verificationWarnings = 0;
        int retriesExhausted = 0;
        int toolErrors = 0;
        int permissionDenials = 0;
        int doomLoops = 0;
        int stepsSkippedNoToolMatch = 0;
        var reasons = new List<string>();

        foreach (var message in history)
        {
            var content = message.Content;
            if (string.IsNullOrEmpty(content))
                continue;

            if (content.Contains(SyntheticMarkers.VerificationWarningMarker, StringComparison.Ordinal))
            {
                verificationWarnings++;
                reasons.Add(Excerpt(content));
            }
            else if (content.Contains("[PlanStep ", StringComparison.Ordinal)
                && content.Contains("Exceeded", StringComparison.Ordinal)
                && content.Contains("attempts", StringComparison.Ordinal))
            {
                retriesExhausted++;
                reasons.Add(Excerpt(content));
            }
            else if (content.Contains("[Tool ", StringComparison.Ordinal)
                && content.Contains(" failed:", StringComparison.Ordinal))
            {
                toolErrors++;
                reasons.Add(Excerpt(content));
            }
            else if (content.Contains(SyntheticMarkers.PermissionDeniedMarker, StringComparison.Ordinal))
            {
                permissionDenials++;
                reasons.Add(Excerpt(content));
            }
            else if (content.Contains("[DoomLoop]", StringComparison.Ordinal))
            {
                doomLoops++;
                reasons.Add(Excerpt(content));
            }
            else if (content.Contains("[PlanStep ", StringComparison.Ordinal)
                && content.Contains("No tool matched", StringComparison.Ordinal)
                && content.Contains("skipping", StringComparison.Ordinal))
            {
                stepsSkippedNoToolMatch++;
                reasons.Add(Excerpt(content));
            }
        }

        // Keep at most the 3 most recent reasons
        var trimmedReasons = reasons.Skip(Math.Max(0, reasons.Count - 3)).ToArray();

        return new Findings(
            verificationWarnings,
            retriesExhausted,
            toolErrors,
            permissionDenials,
            doomLoops,
            stepsSkippedNoToolMatch,
            trimmedReasons);
    }

    /// <summary>
    /// Builds a grounding instruction that prevents the LLM from claiming success when
    /// steps failed. Returns an empty string when <see cref="Findings.HasFailures"/> is false.
    /// </summary>
    public static string BuildGroundingMessage(Findings findings)
    {
        if (!findings.HasFailures)
            return "";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[PlanResult] Some steps did not complete successfully:");

        if (findings.VerificationWarnings > 0)
            sb.AppendLine($"- Verification failures: {findings.VerificationWarnings}");
        if (findings.RetriesExhausted > 0)
            sb.AppendLine($"- Step retries exhausted: {findings.RetriesExhausted}");
        if (findings.ToolErrors > 0)
            sb.AppendLine($"- Tool errors: {findings.ToolErrors}");
        if (findings.PermissionDenials > 0)
            sb.AppendLine($"- Permission denials: {findings.PermissionDenials}");
        if (findings.DoomLoops > 0)
            sb.AppendLine($"- Doom loops: {findings.DoomLoops}");
        if (findings.StepsSkippedNoToolMatch > 0)
            sb.AppendLine($"- Steps skipped (no matching tool): {findings.StepsSkippedNoToolMatch}");

        if (findings.ExcerptedReasons.Count > 0)
        {
            var joined = string.Join(" | ", findings.ExcerptedReasons);
            sb.AppendLine($"Reasons (most recent 3): {joined}");
        }

        sb.AppendLine();
        sb.Append("When summarizing, accurately report these failures. Do NOT claim success for any step that failed. Quote the failure reason if useful for the user.");

        return sb.ToString();
    }

    private static string Excerpt(string content)
    {
        if (content.Length <= 200)
            return content;
        return content[..200];
    }
}
