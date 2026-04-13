namespace Nexus.Hardware.States;

/// <summary>
/// Compatibility between the OS architecture and the process architecture, affecting inference performance.
/// </summary>
public enum ArchitectureState
{
    NativeOptimal,
    NativeCompatible,
    EmulatedPenalty,
    Unsupported
}
