namespace Nexus.Hardware.States;

/// <summary>
/// Current resource pressure on a hardware subsystem (RAM or GPU VRAM), from idle to critical.
/// </summary>
public enum PressureLevel
{
    None,
    Low,
    Medium,
    High,
    Critical
}
