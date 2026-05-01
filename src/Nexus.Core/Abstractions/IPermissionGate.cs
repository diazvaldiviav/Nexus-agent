namespace Nexus.Core.Abstractions;

/// <summary>
/// Gate that decides whether a tool invocation is permitted.
/// Implementations vary by host: CLI prompts the user interactively;
/// Desktop may auto-approve or auto-deny based on model tier.
/// </summary>
public interface IPermissionGate
{
    Task<PermissionGateResponse> RequestAsync(PermissionRequest request, CancellationToken ct);
}

/// <summary>
/// Describes a permission check request for a single tool invocation.
/// </summary>
/// <param name="ServerName">MCP server that owns the tool.</param>
/// <param name="ToolName">Name of the tool being invoked.</param>
/// <param name="Arguments">Arguments the agent intends to pass to the tool (may be null).</param>
/// <param name="Patterns">File-path patterns extracted from the arguments (["*"] when unknown).</param>
/// <param name="Rationale">Human-readable rationale from the planner or agent for why the tool is needed.</param>
public sealed record PermissionRequest(
    string ServerName,
    string ToolName,
    IReadOnlyDictionary<string, object>? Arguments,
    IReadOnlyList<string> Patterns,
    string Rationale);

/// <summary>
/// Decision returned by <see cref="IPermissionGate.RequestAsync"/>.
/// </summary>
public enum PermissionDecision
{
    Allow,
    AllowForSession,
    AllowPersisted,
    Deny,
    DenyWithFeedback
}

/// <summary>
/// Response from <see cref="IPermissionGate.RequestAsync"/>.
/// When <see cref="Decision"/> is <see cref="PermissionDecision.DenyWithFeedback"/>,
/// <see cref="Feedback"/> carries a message to surface to the user.
/// </summary>
public sealed record PermissionGateResponse(PermissionDecision Decision, string? Feedback = null);

/// <summary>
/// Carries a feedback string that the gate wants surfaced to the user.
/// Used when an implementation needs to pass a denial reason through the system.
/// </summary>
public sealed record PermissionFeedback(string Message);
