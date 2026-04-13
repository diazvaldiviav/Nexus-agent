namespace Nexus.Hardware.States;

/// <summary>
/// Recommended execution placement for model inference across available compute resources.
/// </summary>
public enum PlacementStrategy
{
    CpuOnly,
    GpuFull,
    GpuPartial,
    HybridFallback
}
