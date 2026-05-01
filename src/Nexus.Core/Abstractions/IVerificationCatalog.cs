namespace Nexus.Core.Abstractions;

/// <summary>
/// Describes the verification strategy for a mutating tool.
/// </summary>
public enum VerificationMethod
{
    None,
    SnapshotDiff,
    ResponseShape,
    ResponseKeywords
}

/// <summary>
/// Specifies how to capture a pre/post snapshot for SnapshotDiff verification.
/// </summary>
public sealed class SnapshotSpec
{
    public string Tool { get; set; } = "";
    public Dictionary<string, string> Args { get; set; } = new();
    public string Compare { get; set; } = "not_equal";
}

/// <summary>
/// Describes the verification contract for a specific (server, tool) pair.
/// </summary>
public sealed class VerificationRule
{
    public string Server { get; set; } = "";
    public string Tool { get; set; } = "";
    public bool Mutates { get; set; }
    public VerificationMethod Method { get; set; } = VerificationMethod.None;
    public SnapshotSpec? Snapshot { get; set; }

    /// <summary>
    /// When true, an empty post-snapshot result is treated as a verification failure.
    /// </summary>
    public bool EmptyPostIsFailure { get; set; }

    public List<string> SuccessKeywords { get; set; } = new();
    public List<string> FailureKeywords { get; set; } = new();
    public List<string> RequiredFields { get; set; } = new();

    /// <summary>
    /// When true, the tool is destructive (irreversible side-effect such as file delete or move).
    /// Triggers the permission gate to require user confirmation.
    /// </summary>
    public bool Destructive { get; init; }
}

/// <summary>
/// Provides lookup of verification rules for (server, tool) pairs.
/// Bundled rules can be overridden per-user via ~/.nexus/catalog/*.yaml.
/// </summary>
public interface IVerificationCatalog
{
    /// <summary>Total number of loaded verification rules.</summary>
    int Count { get; }

    /// <summary>
    /// Returns the verification rule for the given server and tool, or null if no rule exists.
    /// Lookup is case-insensitive on both server and tool name.
    /// </summary>
    VerificationRule? GetRule(string server, string tool);
}
