namespace Nexus.Core.Services;

/// <summary>
/// Centralized list of synthetic message prefixes injected by AgentService internals.
/// Used to filter planner/tool messages out of user-visible conversation history.
/// </summary>
internal static class SyntheticMarkers
{
    /// <summary>Bare marker token used in StartsWith checks.</summary>
    public const string VerificationWarningMarker = "[VerificationWarning]";

    /// <summary>Marker with a trailing space, used as a decoration prefix in formatted strings.</summary>
    public const string VerificationWarningPrefix = VerificationWarningMarker + " ";

    /// <summary>Bare marker token injected when a permission gate denies a tool invocation.</summary>
    public const string PermissionDeniedMarker = "[PermissionDenied]";

    /// <summary>Marker with a trailing space, used as a decoration prefix for the denial reason string.</summary>
    public const string PermissionDeniedPrefix = PermissionDeniedMarker + " ";

    /// <summary>Bare marker token injected when Layer 4 output fidelity verification determines the LLM summary diverges from read-tool results.</summary>
    public const string FidelityWarningMarker = "[FidelityWarning]";

    /// <summary>Marker with a trailing space, used as a decoration prefix in fidelity-grounding and final-warning strings.</summary>
    public const string FidelityWarningPrefix = FidelityWarningMarker + " ";

    public static readonly string[] Prefixes =
    {
        "[PLANNER] ",
        "[Plan]",
        "[PlanStep ",
        "[Tool result for step ",
        "[Tool Result for ",
        "[Executing tool: ",
        "[DoomLoop]",
        VerificationWarningMarker,
        PermissionDeniedMarker,
        FidelityWarningMarker
    };

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="content"/> starts with any
    /// known synthetic-marker prefix.
    /// </summary>
    public static bool IsSynthetic(string? content)
    {
        if (string.IsNullOrEmpty(content)) return false;
        foreach (var prefix in Prefixes)
        {
            if (content.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
