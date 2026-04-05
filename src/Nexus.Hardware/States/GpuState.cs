namespace Nexus.Hardware.States;

/// <summary>
/// Classified GPU capability tier for inference offloading, from absent to high-performance.
/// </summary>
public enum GpuState
{
    None,
    Limited,
    Capable,
    Strong
}
