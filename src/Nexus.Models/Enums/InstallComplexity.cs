namespace Nexus.Models.Enums;

/// <summary>
/// Complexity level required to install and configure the model for local execution.
/// </summary>
public enum InstallComplexity
{
    /// <summary>One-command install with no manual configuration.</summary>
    Low,
    /// <summary>Requires some manual setup steps.</summary>
    Medium,
    /// <summary>Requires significant manual configuration or dependencies.</summary>
    High
}
