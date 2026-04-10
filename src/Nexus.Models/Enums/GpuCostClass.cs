namespace Nexus.Models.Enums;

/// <summary>
/// Relative GPU VRAM cost during model inference, from none to high utilization.
/// </summary>
public enum GpuCostClass
{
    None,
    Low,
    Medium,
    High
}
