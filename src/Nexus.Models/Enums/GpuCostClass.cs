namespace Nexus.Models.Enums;

/// <summary>
/// Relative GPU VRAM cost during model inference, from none to high utilization.
/// </summary>
public enum GpuCostClass
{
    /// <summary>No GPU required.</summary>
    None,
    /// <summary>Minimal GPU utilization.</summary>
    Low,
    /// <summary>Moderate GPU utilization.</summary>
    Medium,
    /// <summary>Heavy GPU utilization; requires a dedicated GPU with substantial VRAM.</summary>
    High
}
