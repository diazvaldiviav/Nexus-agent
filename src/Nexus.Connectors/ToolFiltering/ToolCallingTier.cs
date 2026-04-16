namespace Nexus.Connectors.ToolFiltering;

/// <summary>
/// Model capability tier for tool calling, derived from parameter count.
/// </summary>
public enum ToolCallingTier
{
    /// <summary>Models below 3B parameters — only Simple tools allowed.</summary>
    Limited,
    /// <summary>Models 3B–8B parameters — Simple and Moderate tools allowed.</summary>
    Capable,
    /// <summary>Models 8B+ or unknown — all tools allowed.</summary>
    Full
}
