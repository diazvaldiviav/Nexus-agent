namespace Nexus.Hardware.Monitoring;

/// <summary>
/// Point-in-time OS-level health metrics for CPU usage, available RAM, and paging activity.
/// </summary>
/// <param name="CpuUsagePercent">Overall CPU utilization as a percentage (0-100).</param>
/// <param name="AvailableRamMb">Available physical RAM in megabytes.</param>
/// <param name="PagesPerSecond">Memory pages per second, indicating paging pressure.</param>
/// <param name="ReadAt">UTC timestamp when the snapshot was captured.</param>
public record SystemHealthSnapshot(
    float CpuUsagePercent,
    float AvailableRamMb,
    float PagesPerSecond,
    DateTime ReadAt);
