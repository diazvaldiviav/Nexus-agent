using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Nexus.Hardware.Abstractions;
using Nexus.Hardware.Monitoring;
using Nexus.Hardware.Windows.Internals;

namespace Nexus.Hardware.Windows.Monitoring;

[SupportedOSPlatform("windows")]
internal sealed class LhmSensorMonitor : ISensorMonitor, IDisposable
{
    private readonly ILhmComputer _computer;
    private readonly ILogger<LhmSensorMonitor>? _logger;

    public LhmSensorMonitor(ILhmComputer computer, ILogger<LhmSensorMonitor>? logger = null)
    {
        _computer = computer ?? throw new ArgumentNullException(nameof(computer));
        _logger = logger;

        IsAvailable = _computer.TryOpen();

        if (!IsAvailable)
        {
            _logger?.LogWarning("LibreHardwareMonitor sensors are unavailable; sensor readings will return null values");
        }
    }

    public bool IsAvailable { get; }

    public async Task<SensorSnapshot> ReadAsync(CancellationToken ct = default)
    {
        if (!IsAvailable)
            return new SensorSnapshot(null, null, null, null, null, DateTime.UtcNow);

        return await Task.Run(() => ReadCore(), ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_computer is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private SensorSnapshot ReadCore()
    {
        try
        {
            var sensors = _computer.ReadSensors();

            var cpuTemps = sensors.Where(s => s.HardwareType == LhmHardwareType.Cpu && s.SensorType == LhmSensorType.Temperature).ToList();
            var gpuTemps = sensors.Where(s => s.HardwareType == LhmHardwareType.Gpu && s.SensorType == LhmSensorType.Temperature).ToList();
            var cpuClocks = sensors.Where(s => s.HardwareType == LhmHardwareType.Cpu && s.SensorType == LhmSensorType.Clock).ToList();
            var cpuLoads = sensors.Where(s => s.HardwareType == LhmHardwareType.Cpu && s.SensorType == LhmSensorType.Load).ToList();
            var gpuLoads = sensors.Where(s => s.HardwareType == LhmHardwareType.Gpu && s.SensorType == LhmSensorType.Load).ToList();

            float? cpuTemp = SelectPreferred(cpuTemps, "Package");
            float? gpuTemp = gpuTemps.Count > 0 ? gpuTemps[0].Value : null;
            float? cpuClock = cpuClocks.Count > 0 ? cpuClocks.Max(s => s.Value) : null;
            float? cpuLoad = SelectPreferred(cpuLoads, "Total");
            float? gpuLoad = gpuLoads.Count > 0 ? gpuLoads[0].Value : null;

            return new SensorSnapshot(cpuTemp, gpuTemp, cpuClock, cpuLoad, gpuLoad, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to read sensors from LibreHardwareMonitor");
            return new SensorSnapshot(null, null, null, null, null, DateTime.UtcNow);
        }
    }

    private static float? SelectPreferred(List<LhmSensorReading> readings, string preferredNameContains)
    {
        if (readings.Count == 0)
            return null;

        var preferred = readings.FirstOrDefault(s => s.SensorName.Contains(preferredNameContains, StringComparison.OrdinalIgnoreCase));
        return preferred != default ? preferred.Value : readings[0].Value;
    }
}
