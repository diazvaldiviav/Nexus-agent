using Nexus.Hardware.Monitoring;

namespace Nexus.Hardware.Abstractions;

public interface ISensorMonitor
{
    Task<SensorSnapshot> ReadAsync(CancellationToken ct = default);
    bool IsAvailable { get; }
}
