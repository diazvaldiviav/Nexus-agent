using Nexus.Hardware.Monitoring;

namespace Nexus.Hardware.Tests;

public class SensorSnapshotTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var readAt = new DateTime(2026, 4, 5, 12, 0, 0, DateTimeKind.Utc);
        var snapshot = new SensorSnapshot(65.5f, 72.0f, 4200.0f, 45.0f, 80.0f, readAt);

        Assert.Equal(65.5f, snapshot.CpuTemperatureCelsius);
        Assert.Equal(72.0f, snapshot.GpuTemperatureCelsius);
        Assert.Equal(4200.0f, snapshot.CpuClockSpeedMhz);
        Assert.Equal(45.0f, snapshot.CpuLoadPercent);
        Assert.Equal(80.0f, snapshot.GpuLoadPercent);
        Assert.Equal(readAt, snapshot.ReadAt);
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var readAt = new DateTime(2026, 4, 5, 12, 0, 0, DateTimeKind.Utc);
        var a = new SensorSnapshot(65.5f, 72.0f, 4200.0f, 45.0f, 80.0f, readAt);
        var b = new SensorSnapshot(65.5f, 72.0f, 4200.0f, 45.0f, 80.0f, readAt);

        Assert.Equal(a, b);
    }
}
