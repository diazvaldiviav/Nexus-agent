using Nexus.Hardware.Envelopes;

namespace Nexus.Hardware.Abstractions;

/// <summary>
/// Profiles GPU capabilities for VRAM budget assessment and layer offloading.
/// </summary>
public interface IGpuProfiler
{
    /// <summary>
    /// Captures a snapshot of GPU capabilities and returns a <see cref="GpuEnvelope"/>.
    /// </summary>
    Task<GpuEnvelope> ProfileAsync(CancellationToken ct = default);
}
