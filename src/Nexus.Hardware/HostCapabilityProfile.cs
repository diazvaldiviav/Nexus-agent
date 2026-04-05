using System.Runtime.InteropServices;
using Nexus.Hardware.Envelopes;
using Nexus.Hardware.States;

namespace Nexus.Hardware;

/// <summary>
/// Aggregate hardware profile combining CPU, RAM, and GPU envelopes with their classified states,
/// OS metadata, and the timestamp when profiling occurred.
/// </summary>
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
