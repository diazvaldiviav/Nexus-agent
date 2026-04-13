using System.Runtime.InteropServices;
using Nexus.Hardware.Envelopes;
using Nexus.Hardware.States;

namespace Nexus.Hardware;

/// <summary>
/// Aggregate hardware profile combining CPU, RAM, and GPU envelopes with their classified states,
/// OS metadata, and the timestamp when profiling occurred.
/// </summary>
/// <param name="Cpu">CPU capability envelope.</param>
/// <param name="Ram">RAM capability envelope.</param>
/// <param name="Gpu">GPU/VRAM capability envelope.</param>
/// <param name="CpuState">Classified CPU capability tier.</param>
/// <param name="RamState">Classified RAM capability tier.</param>
/// <param name="GpuState">Classified GPU capability tier.</param>
/// <param name="ArchitectureState">Classified architecture compatibility state.</param>
/// <param name="OsVersion">Operating system version string.</param>
/// <param name="OsArchitecture">OS-level processor architecture.</param>
/// <param name="ProcessArchitecture">Current process architecture (may differ from OS under emulation).</param>
/// <param name="ProfiledAt">UTC timestamp when this profile was captured.</param>
public record HostCapabilityProfile(
    CpuEnvelope Cpu,
    RamEnvelope Ram,
    GpuEnvelope Gpu,
    CpuState CpuState,
    RamState RamState,
    GpuState GpuState,
    ArchitectureState ArchitectureState,
    string OsVersion,
    Architecture OsArchitecture,
    Architecture ProcessArchitecture,
    DateTime ProfiledAt
    );
