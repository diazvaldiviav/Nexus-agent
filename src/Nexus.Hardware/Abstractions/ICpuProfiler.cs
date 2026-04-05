using Nexus.Hardware.Envelopes;

namespace Nexus.Hardware.Abstractions;

/// <summary>
/// Profiles CPU capabilities for LLM inference suitability assessment.
/// </summary>
public interface ICpuProfiler
{
    /// <summary>
    /// Captures a snapshot of CPU capabilities and returns a <see cref="CpuEnvelope"/>.
    /// </summary>
    Task<CpuEnvelope> ProfileAsync(CancellationToken ct = default);
}
