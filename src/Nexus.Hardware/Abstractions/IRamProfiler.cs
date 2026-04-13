using Nexus.Hardware.Envelopes;

namespace Nexus.Hardware.Abstractions;

/// <summary>
/// Profiles system RAM availability and computes safe memory budgets for model loading.
/// </summary>
public interface IRamProfiler
{
    /// <summary>
    /// Captures a snapshot of RAM availability and returns a <see cref="RamEnvelope"/>.
    /// </summary>
    Task<RamEnvelope> ProfileAsync(CancellationToken ct = default);
}
