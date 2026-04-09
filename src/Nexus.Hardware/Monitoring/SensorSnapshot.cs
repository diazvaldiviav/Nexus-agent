namespace Nexus.Hardware.Monitoring;

/// <summary>
/// Point-in-time thermal and load telemetry from platform sensor providers.
/// </summary>
/// <param name="CpuTemperatureCelsius">CPU package temperature in degrees Celsius, or null if unavailable.</param>
/// <param name="GpuTemperatureCelsius">GPU temperature in degrees Celsius, or null if unavailable.</param>
/// <param name="CpuClockSpeedMhz">Current CPU clock speed in MHz, or null if unavailable.</param>
/// <param name="CpuLoadPercent">CPU load as a percentage (0-100), or null if unavailable.</param>
/// <param name="GpuLoadPercent">GPU load as a percentage (0-100), or null if unavailable.</param>
/// <param name="ReadAt">UTC timestamp when the snapshot was captured.</param>
public record SensorSnapshot(
    float? CpuTemperatureCelsius,
    float? GpuTemperatureCelsius,
    float? CpuClockSpeedMhz,
    float? CpuLoadPercent,
    float? GpuLoadPercent,
    DateTime ReadAt);
