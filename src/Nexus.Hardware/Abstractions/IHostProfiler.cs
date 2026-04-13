namespace Nexus.Hardware.Abstractions;

/// <summary>
/// Orchestrates full host profiling by combining CPU, RAM, and GPU profilers
/// into a complete <see cref="HostCapabilityProfile"/>.
/// </summary>
public interface IHostProfiler
{
    /// <summary>
    /// Builds a complete host capability profile from all hardware subsystems.
    /// </summary>
    Task<HostCapabilityProfile> BuildProfileAsync(CancellationToken ct = default);
}
