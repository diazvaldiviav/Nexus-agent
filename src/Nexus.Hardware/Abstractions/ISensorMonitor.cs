using Nexus.Hardware.Monitoring;

namespace Nexus.Hardware.Abstractions;

/// <summary>
/// Reads live thermal and load telemetry from platform-specific sensor providers.
/// </summary>
public interface ISensorMonitor
{
    /// <summary>
    /// Reads a point-in-time snapshot of CPU/GPU temperatures and load percentages.
    /// </summary>
    Task<SensorSnapshot> ReadAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets a value indicating whether the sensor provider is accessible on this platform.
    /// </summary>
    bool IsAvailable { get; }
}
