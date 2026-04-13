using Nexus.Hardware.Tests.Fakes;
using Nexus.Hardware.Windows.Internals;
using Nexus.Hardware.Windows.Monitoring;

namespace Nexus.Hardware.Tests;

public class LhmSensorMonitorTests
{
    private static LhmSensorReading MakeReading(
        LhmHardwareType hw,
        LhmSensorType sensor,
        string name,
        float value)
        => new(hw, sensor, name, value);

    [Fact]
    public async Task ReadAsync_Unavailable_AllNullSnapshot()
    {
        // Arrange
        var fake = FakeLhmComputer.Unavailable();
        using var monitor = new LhmSensorMonitor(fake);

        // Act
        var snapshot = await monitor.ReadAsync();

        // Assert
        Assert.Null(snapshot.CpuTemperatureCelsius);
        Assert.Null(snapshot.GpuTemperatureCelsius);
        Assert.Null(snapshot.CpuClockSpeedMhz);
        Assert.Null(snapshot.CpuLoadPercent);
        Assert.Null(snapshot.GpuLoadPercent);
        Assert.True(snapshot.ReadAt <= DateTime.UtcNow);
    }

    [Fact]
    public void IsAvailable_OpenSucceeds_True()
    {
        // Arrange & Act
        var fake = new FakeLhmComputer(true);
        using var monitor = new LhmSensorMonitor(fake);

        // Assert
        Assert.True(monitor.IsAvailable);
    }

    [Fact]
    public void IsAvailable_OpenFails_False()
    {
        // Arrange & Act
        var fake = new FakeLhmComputer(false);
        using var monitor = new LhmSensorMonitor(fake);

        // Assert
        Assert.False(monitor.IsAvailable);
    }

    [Fact]
    public async Task ReadAsync_AllSensors_MapsCorrectly()
    {
        // Arrange
        var fake = new FakeLhmComputer(true,
            MakeReading(LhmHardwareType.Cpu, LhmSensorType.Temperature, "CPU Package", 65.5f),
            MakeReading(LhmHardwareType.Gpu, LhmSensorType.Temperature, "GPU Core", 72.0f),
            MakeReading(LhmHardwareType.Cpu, LhmSensorType.Clock, "CPU Core #1", 4200f),
            MakeReading(LhmHardwareType.Cpu, LhmSensorType.Load, "CPU Total", 45.0f),
            MakeReading(LhmHardwareType.Gpu, LhmSensorType.Load, "GPU Core", 80.0f));
        using var monitor = new LhmSensorMonitor(fake);

        // Act
        var snapshot = await monitor.ReadAsync();

        // Assert
        Assert.Equal(65.5f, snapshot.CpuTemperatureCelsius);
        Assert.Equal(72.0f, snapshot.GpuTemperatureCelsius);
        Assert.Equal(4200f, snapshot.CpuClockSpeedMhz);
        Assert.Equal(45.0f, snapshot.CpuLoadPercent);
        Assert.Equal(80.0f, snapshot.GpuLoadPercent);
    }

    [Fact]
    public async Task ReadAsync_PartialSensors_RemainingNull()
    {
        // Arrange
        var fake = new FakeLhmComputer(true,
            MakeReading(LhmHardwareType.Cpu, LhmSensorType.Temperature, "CPU Package", 55.0f));
        using var monitor = new LhmSensorMonitor(fake);

        // Act
        var snapshot = await monitor.ReadAsync();

        // Assert
        Assert.Equal(55.0f, snapshot.CpuTemperatureCelsius);
        Assert.Null(snapshot.GpuTemperatureCelsius);
        Assert.Null(snapshot.CpuClockSpeedMhz);
        Assert.Null(snapshot.CpuLoadPercent);
        Assert.Null(snapshot.GpuLoadPercent);
    }

    [Fact]
    public async Task ReadAsync_EmptySensors_AllNullSnapshot()
    {
        // Arrange
        var fake = new FakeLhmComputer(true);
        using var monitor = new LhmSensorMonitor(fake);

        // Act
        var snapshot = await monitor.ReadAsync();

        // Assert
        Assert.Null(snapshot.CpuTemperatureCelsius);
        Assert.Null(snapshot.GpuTemperatureCelsius);
        Assert.Null(snapshot.CpuClockSpeedMhz);
        Assert.Null(snapshot.CpuLoadPercent);
        Assert.Null(snapshot.GpuLoadPercent);
    }

    [Fact]
    public async Task ReadAsync_ReadThrows_ReturnsNullSnapshot()
    {
        // Arrange
        var fake = FakeLhmComputer.Throwing(new InvalidOperationException("sensor failure"));
        using var monitor = new LhmSensorMonitor(fake);

        // Act
        var snapshot = await monitor.ReadAsync();

        // Assert
        Assert.Null(snapshot.CpuTemperatureCelsius);
        Assert.Null(snapshot.GpuTemperatureCelsius);
        Assert.Null(snapshot.CpuClockSpeedMhz);
        Assert.Null(snapshot.CpuLoadPercent);
        Assert.Null(snapshot.GpuLoadPercent);
    }

    [Fact]
    public async Task ReadAsync_MultipleCpuClocks_TakesMax()
    {
        // Arrange
        var fake = new FakeLhmComputer(true,
            MakeReading(LhmHardwareType.Cpu, LhmSensorType.Clock, "CPU Core #1", 3500f),
            MakeReading(LhmHardwareType.Cpu, LhmSensorType.Clock, "CPU Core #2", 4200f));
        using var monitor = new LhmSensorMonitor(fake);

        // Act
        var snapshot = await monitor.ReadAsync();

        // Assert
        Assert.Equal(4200f, snapshot.CpuClockSpeedMhz);
    }

    [Fact]
    public async Task ReadAsync_CpuTempFallback_NoPackage()
    {
        // Arrange — no "Package" in name, should fall back to first
        var fake = new FakeLhmComputer(true,
            MakeReading(LhmHardwareType.Cpu, LhmSensorType.Temperature, "Core #1", 58.0f));
        using var monitor = new LhmSensorMonitor(fake);

        // Act
        var snapshot = await monitor.ReadAsync();

        // Assert
        Assert.Equal(58.0f, snapshot.CpuTemperatureCelsius);
    }

    [Fact]
    public async Task ReadAsync_ReadAtIsRecent()
    {
        // Arrange
        var before = DateTime.UtcNow;
        var fake = new FakeLhmComputer(true,
            MakeReading(LhmHardwareType.Cpu, LhmSensorType.Temperature, "CPU Package", 60f));
        using var monitor = new LhmSensorMonitor(fake);

        // Act
        var snapshot = await monitor.ReadAsync();

        // Assert
        var elapsed = snapshot.ReadAt - before;
        Assert.True(elapsed.TotalSeconds < 5, $"ReadAt should be within 5 seconds, was {elapsed.TotalSeconds}s");
    }

    [Fact]
    public void Constructor_NullComputer_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new LhmSensorMonitor(null!));
    }

    [Fact]
    public void Dispose_DisposesComputer()
    {
        // Arrange
        var fake = new FakeLhmComputer(true);
        var monitor = new LhmSensorMonitor(fake);

        // Act
        monitor.Dispose();

        // Assert
        Assert.True(fake.Disposed);
    }
}
