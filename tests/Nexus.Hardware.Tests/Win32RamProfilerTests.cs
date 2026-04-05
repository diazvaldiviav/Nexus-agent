using Nexus.Hardware.States;
using Nexus.Hardware.Tests.Fakes;
using Nexus.Hardware.Windows.Internals;
using Nexus.Hardware.Windows.Profilers;

namespace Nexus.Hardware.Tests;

public class Win32RamProfilerTests
{
    private static MemoryStatusResult MakeStatus(
        uint load,
        ulong total,
        ulong avail,
        ulong totalPage = 0,
        ulong availPage = 0) => new(load, total, avail, totalPage, availPage);

    [Fact]
    public async Task ProfileAsync_32GB_20GBAvail_CorrectBudgets()
    {
        // Arrange
        const ulong totalPhysical = 32UL * 1024 * 1024 * 1024;
        const ulong availPhysical = 20UL * 1024 * 1024 * 1024;
        var status = MakeStatus(40, totalPhysical, availPhysical);
        var profiler = new TestableRamProfiler(status);

        // Act
        var envelope = await profiler.ProfileAsync();

        // Assert
        Assert.Equal((long)availPhysical, envelope.UsableRamNow);
        Assert.Equal((long)(availPhysical * 0.70), envelope.SafeModelRamBudget);
        Assert.Equal((long)((long)(availPhysical * 0.70) * 0.85), envelope.SafeInferenceRamBudget);
        Assert.Equal(PressureLevel.None, envelope.RamPressureLevel);
        Assert.True(envelope.IsViable());
    }

    [Fact]
    public async Task ProfileAsync_8GB_1GBAvail_HighPressure()
    {
        // Arrange
        const ulong totalPhysical = 8UL * 1024 * 1024 * 1024;
        const ulong availPhysical = 1UL * 1024 * 1024 * 1024;
        var status = MakeStatus(88, totalPhysical, availPhysical);
        var profiler = new TestableRamProfiler(status);

        // Act
        var envelope = await profiler.ProfileAsync();

        // Assert
        Assert.Equal(PressureLevel.High, envelope.RamPressureLevel);
        Assert.True(envelope.IsViable());
    }

    [Fact]
    public async Task ProfileAsync_ZeroAvailable_NotViable()
    {
        // Arrange
        const ulong totalPhysical = 8UL * 1024 * 1024 * 1024;
        var status = MakeStatus(99, totalPhysical, 0);
        var profiler = new TestableRamProfiler(status);

        // Act
        var envelope = await profiler.ProfileAsync();

        // Assert
        Assert.Equal(0, envelope.UsableRamNow);
        Assert.Equal(0, envelope.SafeModelRamBudget);
        Assert.Equal(0, envelope.SafeInferenceRamBudget);
        Assert.False(envelope.IsViable());
    }

    [Fact]
    public async Task ProfileAsync_ExceptionThrown_DegradedEnvelope()
    {
        // Arrange
        var profiler = TestableRamProfiler.Throwing(new InvalidOperationException("P/Invoke failed"));

        // Act
        var envelope = await profiler.ProfileAsync();

        // Assert
        Assert.Equal(0, envelope.UsableRamNow);
        Assert.Equal(0, envelope.SafeModelRamBudget);
        Assert.Equal(0, envelope.SafeInferenceRamBudget);
        Assert.Equal(PressureLevel.Critical, envelope.RamPressureLevel);
        Assert.False(envelope.IsViable());
    }

    [Theory]
    [InlineData(0u, PressureLevel.None)]
    [InlineData(49u, PressureLevel.None)]
    [InlineData(50u, PressureLevel.Low)]
    [InlineData(69u, PressureLevel.Low)]
    [InlineData(70u, PressureLevel.Medium)]
    [InlineData(84u, PressureLevel.Medium)]
    [InlineData(85u, PressureLevel.High)]
    [InlineData(94u, PressureLevel.High)]
    [InlineData(95u, PressureLevel.Critical)]
    [InlineData(100u, PressureLevel.Critical)]
    public async Task ClassifyPressure_BoundaryValues(uint memoryLoad, PressureLevel expectedPressure)
    {
        // Arrange — use 16GB total, 8GB available as baseline; only load varies
        const ulong totalPhysical = 16UL * 1024 * 1024 * 1024;
        const ulong availPhysical = 8UL * 1024 * 1024 * 1024;
        var status = MakeStatus(memoryLoad, totalPhysical, availPhysical);
        var profiler = new TestableRamProfiler(status);

        // Act
        var envelope = await profiler.ProfileAsync();

        // Assert
        Assert.Equal(expectedPressure, envelope.RamPressureLevel);
    }

    [Fact]
    public void Constructor_NoException()
    {
        // Arrange & Act — direct instantiation does not throw
        var profiler = new Win32RamProfiler();

        // Assert
        Assert.NotNull(profiler);
    }
}
