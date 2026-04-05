namespace Nexus.Hardware.Monitoring;

public record SensorSnapshot(
    float? CpuTemperatureCelsius,
    float? GpuTemperatureCelsius,
    float? CpuClockSpeedMhz,
    float? CpuLoadPercent,
    float? GpuLoadPercent,
    DateTime ReadAt);
