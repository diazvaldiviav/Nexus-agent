namespace Nexus.Models.Enums;

/// <summary>
/// Relative CPU resource cost during model inference, from minimal to very demanding.
/// </summary>
public enum CpuCostClass
{
    Low,
    Medium,
    High,
    VeryHigh
}
