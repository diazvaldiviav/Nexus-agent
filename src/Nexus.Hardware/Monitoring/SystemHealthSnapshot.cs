namespace Nexus.Hardware.Monitoring;

public record SystemHealthSnapshot(
    float CpuUsagePercent,
    float AvailableRamMb,
    float PagesPerSecond,
    DateTime ReadAt);
