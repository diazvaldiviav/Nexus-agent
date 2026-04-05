using Nexus.Hardware.Monitoring;

namespace Nexus.Hardware.Tests;

public class SystemHealthSnapshotTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var readAt = new DateTime(2026, 4, 5, 12, 0, 0, DateTimeKind.Utc);
        var snapshot = new SystemHealthSnapshot(55.0f, 8192.0f, 12.5f, readAt);

        Assert.Equal(55.0f, snapshot.CpuUsagePercent);
        Assert.Equal(8192.0f, snapshot.AvailableRamMb);
        Assert.Equal(12.5f, snapshot.PagesPerSecond);
        Assert.Equal(readAt, snapshot.ReadAt);
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var readAt = new DateTime(2026, 4, 5, 12, 0, 0, DateTimeKind.Utc);
        var a = new SystemHealthSnapshot(55.0f, 8192.0f, 12.5f, readAt);
        var b = new SystemHealthSnapshot(55.0f, 8192.0f, 12.5f, readAt);

        Assert.Equal(a, b);
    }
}
