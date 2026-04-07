namespace Nexus.Hardware.Windows.Internals;

internal enum LhmHardwareType
{
    Cpu,
    Gpu,
    Other
}

internal enum LhmSensorType
{
    Temperature,
    Clock,
    Load,
    Other
}

internal readonly record struct LhmSensorReading(
    LhmHardwareType HardwareType,
    LhmSensorType SensorType,
    string SensorName,
    float? Value);
