namespace Nexus.Models.Enums;

/// <summary>
/// Relative CPU resource cost during model inference, from minimal to very demanding.
/// </summary>
public enum CpuCostClass
{
    /// <summary>Minimal CPU load; suitable for low-power devices.</summary>
    Low,
    /// <summary>Moderate CPU load; suitable for mainstream desktops.</summary>
    Medium,
    /// <summary>Heavy CPU load; requires a performant multi-core processor.</summary>
    High,
    /// <summary>Extreme CPU load; requires high-end workstation hardware.</summary>
    VeryHigh
}
