using System.Runtime.Versioning;
using LibreHardwareMonitor.Hardware;
using Microsoft.Extensions.Logging;

namespace Nexus.Hardware.Windows.Internals;

[SupportedOSPlatform("windows")]
internal sealed class LhmComputerWrapper : ILhmComputer, IDisposable
{
    private readonly Computer _computer;
    private readonly ILogger<LhmComputerWrapper>? _logger;
    private bool _opened;

    public LhmComputerWrapper(ILogger<LhmComputerWrapper>? logger = null)
    {
        _logger = logger;
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = false
        };
    }

    public bool TryOpen()
    {
        try
        {
            _computer.Open();
            _opened = true;
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger?.LogWarning(ex, "Insufficient permissions to open LibreHardwareMonitor sensors");
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to open LibreHardwareMonitor sensors");
            return false;
        }
    }

    public IReadOnlyList<LhmSensorReading> ReadSensors()
    {
        var readings = new List<LhmSensorReading>();

        foreach (var hardware in _computer.Hardware)
        {
            hardware.Update();

            CollectSensors(hardware, readings);

            foreach (var sub in hardware.SubHardware)
            {
                sub.Update();
                CollectSensors(sub, readings);
            }
        }

        return readings;
    }

    public void Dispose()
    {
        if (_opened)
        {
            _computer.Close();
        }
    }

    private static void CollectSensors(IHardware hardware, List<LhmSensorReading> readings)
    {
        var hwType = MapHardwareType(hardware.HardwareType);
        if (hwType == LhmHardwareType.Other)
            return;

        foreach (var sensor in hardware.Sensors)
        {
            var sensorType = MapSensorType(sensor.SensorType);
            if (sensorType == LhmSensorType.Other)
                continue;

            if (sensor.Value is null)
                continue;

            readings.Add(new LhmSensorReading(hwType, sensorType, sensor.Name, sensor.Value));
        }
    }

    private static LhmHardwareType MapHardwareType(HardwareType type) => type switch
    {
        HardwareType.Cpu => LhmHardwareType.Cpu,
        HardwareType.GpuNvidia => LhmHardwareType.Gpu,
        HardwareType.GpuAmd => LhmHardwareType.Gpu,
        HardwareType.GpuIntel => LhmHardwareType.Gpu,
        _ => LhmHardwareType.Other
    };

    private static LhmSensorType MapSensorType(SensorType type) => type switch
    {
        SensorType.Temperature => LhmSensorType.Temperature,
        SensorType.Clock => LhmSensorType.Clock,
        SensorType.Load => LhmSensorType.Load,
        _ => LhmSensorType.Other
    };
}
