using Nexus.Hardware.Tests.Fakes;
using Nexus.Hardware.Windows.Monitoring;

namespace Nexus.Hardware.Tests;

public class PerfCounterMonitorTests
{
    [Fact]
    public void ReadSnapshot_CorrectValues()
    {
        // Arrange
        var provider = new FakePerfCounterProvider(45.5f, 8192f, 12.5f);
        using var monitor = new PerfCounterMonitor(provider);

        // Act
        var snapshot = monitor.ReadSnapshot();

        // Assert
        Assert.Equal(45.5f, snapshot.CpuUsagePercent);
        Assert.Equal(8192f, snapshot.AvailableRamMb);
        Assert.Equal(12.5f, snapshot.PagesPerSecond);
    }

    [Fact]
    public void ReadSnapshot_ZeroDefaults()
    {
        // Arrange
        var provider = new FakePerfCounterProvider();
        using var monitor = new PerfCounterMonitor(provider);

        // Act
        var snapshot = monitor.ReadSnapshot();

        // Assert
        Assert.Equal(0f, snapshot.CpuUsagePercent);
        Assert.Equal(0f, snapshot.AvailableRamMb);
        Assert.Equal(0f, snapshot.PagesPerSecond);
    }

    [Fact]
    public void ReadSnapshot_ProviderThrows_GracefulZeros()
    {
        // Arrange
        var provider = FakePerfCounterProvider.Throwing(new InvalidOperationException("counter failure"));
        using var monitor = new PerfCounterMonitor(provider);

        // Act
        var snapshot = monitor.ReadSnapshot();

        // Assert
        Assert.Equal(0f, snapshot.CpuUsagePercent);
        Assert.Equal(0f, snapshot.AvailableRamMb);
        Assert.Equal(0f, snapshot.PagesPerSecond);
    }

    [Fact]
    public void ReadSnapshot_ReadAtIsRecent()
    {
        // Arrange
        var before = DateTime.UtcNow;
        var provider = new FakePerfCounterProvider(10f, 4096f, 5f);
        using var monitor = new PerfCounterMonitor(provider);

        // Act
        var snapshot = monitor.ReadSnapshot();

        // Assert
        var elapsed = snapshot.ReadAt - before;
        Assert.True(elapsed.TotalSeconds < 5, $"ReadAt should be within 5 seconds, was {elapsed.TotalSeconds}s");
    }

    [Fact]
    public void Constructor_NullProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PerfCounterMonitor(null!));
    }

    [Fact]
    public void Dispose_DisposesProvider()
    {
        // Arrange
        var provider = new FakePerfCounterProvider(10f, 2048f, 1f);
        var monitor = new PerfCounterMonitor(provider);

        // Act
        monitor.Dispose();

        // Assert
        Assert.True(provider.Disposed);
    }
}
