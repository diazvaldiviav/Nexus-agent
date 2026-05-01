namespace Nexus.Core.Abstractions;

/// <summary>
/// Represents the outcome of a tool verification check.
/// </summary>
public sealed class VerificationOutcome
{
    public bool IsVerified { get; init; }
    public bool RuleMatched { get; init; }
    public string? Reason { get; init; }
    public float Confidence { get; init; }

    public static VerificationOutcome Verified(string? reason = null) =>
        new() { IsVerified = true, RuleMatched = true, Reason = reason, Confidence = 1.0f };

    public static VerificationOutcome Failed(string reason, float confidence = 0.9f) =>
        new() { IsVerified = false, RuleMatched = true, Reason = reason, Confidence = confidence };

    public static VerificationOutcome NoRule() =>
        new() { IsVerified = true, RuleMatched = false, Reason = null, Confidence = 0.0f };
}

/// <summary>
/// Verifies that a mutating tool call had its intended effect by comparing
/// pre/post snapshots or inspecting the tool result text.
/// </summary>
public interface IToolVerifier
{
    /// <summary>
    /// Verifies a tool invocation based on its result and an optional pre-execution snapshot.
    /// </summary>
    Task<VerificationOutcome> VerifyAsync(
        string serverName,
        string toolName,
        IReadOnlyDictionary<string, object>? arguments,
        IReadOnlyDictionary<string, object>? preSnapshot,
        string toolResult,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures a snapshot of the resource state before the tool is invoked.
    /// Returns null when no snapshot rule applies.
    /// </summary>
    Task<IReadOnlyDictionary<string, object>?> CapturePreSnapshotAsync(
        string serverName,
        string toolName,
        IReadOnlyDictionary<string, object>? arguments,
        CancellationToken cancellationToken = default);
}
