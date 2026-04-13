using System.Runtime.InteropServices;
using Nexus.Hardware.Envelopes;
using Nexus.Hardware.States;
using Nexus.Hardware.Tests.Fakes;
using Nexus.Hardware.Windows.Internals;
using Nexus.Hardware.Windows.Profilers;

namespace Nexus.Hardware.Tests;

public class DxgiGpuProfilerTests
{
    private const long OneGB = 1L * 1024 * 1024 * 1024;
    private const long TwoGB = 2L * 1024 * 1024 * 1024;
    private const long EightGB = 8L * 1024 * 1024 * 1024;
    private const long SixteenGB = 16L * 1024 * 1024 * 1024;
    private const long TwentyFourGB = 24L * 1024 * 1024 * 1024;

    private static DxgiAdapterInfo MakeAdapter(
        string desc = "Test GPU",
        uint vendorId = 0x10DE,
        long dedicated = 0,
        long shared = 0,
        long budget = 0,
        long usage = 0,
        bool isHw = true)
        => new(desc, vendorId, dedicated, shared, budget, usage, isHw);

    [Fact]
    public async Task ProfileAsync_Nvidia24GB_CorrectEnvelope()
    {
        // Arrange
        var adapter = MakeAdapter(dedicated: TwentyFourGB, budget: TwentyFourGB, usage: TwoGB);
        var provider = new FakeDxgiAdapterProvider(adapter);
        var profiler = new DxgiGpuProfiler(provider);

        // Act
        var result = await profiler.ProfileAsync();

        // Assert
        var expectedAvail = Math.Max(0, TwentyFourGB - TwoGB); // 22GB
        var expectedSafe = (long)(expectedAvail * 0.85);

        Assert.Equal(expectedAvail, result.UsableLocalVramNow);
        Assert.Equal(expectedSafe, result.SafeGpuBudget);
        Assert.True(result.CanFullOffload);
        Assert.True(result.CanPartialOffload);
        Assert.Equal(PressureLevel.None, result.GpuPressureLevel); // 2/24 < 0.50
    }

    [Fact]
    public async Task ProfileAsync_Integrated2GB_LimitedOffload()
    {
        // Arrange — budget=0 means avail falls back to dedicated
        var adapter = MakeAdapter(dedicated: TwoGB, budget: 0);
        var provider = new FakeDxgiAdapterProvider(adapter);
        var profiler = new DxgiGpuProfiler(provider);

        // Act
        var result = await profiler.ProfileAsync();

        // Assert
        var expectedSafe = (long)(TwoGB * 0.85); // 1825361100
        Assert.Equal(TwoGB, result.UsableLocalVramNow);
        Assert.Equal(expectedSafe, result.SafeGpuBudget);
        Assert.False(result.CanFullOffload);  // expectedSafe < 4GB
        Assert.True(result.CanPartialOffload); // expectedSafe > 1GB
        Assert.Equal(PressureLevel.None, result.GpuPressureLevel); // budget=0 -> None
    }

    [Fact]
    public async Task ProfileAsync_NoAdapters_ReturnsNoGpu()
    {
        // Arrange
        var provider = new FakeDxgiAdapterProvider();
        var profiler = new DxgiGpuProfiler(provider);

        // Act
        var result = await profiler.ProfileAsync();

        // Assert
        Assert.Equal(GpuEnvelope.NoGpu(), result);
    }

    [Fact]
    public async Task ProfileAsync_MultiGpu_PicksHighestVram()
    {
        // Arrange
        var small = MakeAdapter(desc: "Small GPU", dedicated: EightGB);
        var large = MakeAdapter(desc: "Large GPU", dedicated: TwentyFourGB);
        var provider = new FakeDxgiAdapterProvider(small, large);
        var profiler = new DxgiGpuProfiler(provider);

        // Act
        var result = await profiler.ProfileAsync();

        // Assert — uses 24GB adapter; budget=0 so avail=dedicated
        Assert.Equal(TwentyFourGB, result.UsableLocalVramNow);
        Assert.Equal((long)(TwentyFourGB * 0.85), result.SafeGpuBudget);
    }

    [Fact]
    public async Task ProfileAsync_NoBudgetInfo_FallbackToDedicated()
    {
        // Arrange
        var adapter = MakeAdapter(dedicated: SixteenGB, budget: 0);
        var provider = new FakeDxgiAdapterProvider(adapter);
        var profiler = new DxgiGpuProfiler(provider);

        // Act
        var result = await profiler.ProfileAsync();

        // Assert
        Assert.Equal(SixteenGB, result.UsableLocalVramNow);
        Assert.Equal((long)(SixteenGB * 0.85), result.SafeGpuBudget);
        Assert.True(result.CanFullOffload);  // safe > 4GB
        Assert.Equal(PressureLevel.None, result.GpuPressureLevel); // budget=0 -> None
    }

    [Fact]
    public async Task ProfileAsync_Exception_ReturnsNoGpu()
    {
        // Arrange
        var provider = FakeDxgiAdapterProvider.Throwing(new COMException("DXGI error"));
        var profiler = new DxgiGpuProfiler(provider);

        // Act
        var result = await profiler.ProfileAsync();

        // Assert
        Assert.Equal(GpuEnvelope.NoGpu(), result);
    }

    [Theory]
    [InlineData(0L, PressureLevel.None)]
    [InlineData(4_900_000_000L, PressureLevel.None)]       // ratio=0.49, below Low (>=0.50)
    [InlineData(4_999_999_999L, PressureLevel.None)]       // just below 0.50 boundary
    [InlineData(5_000_000_000L, PressureLevel.Low)]        // ratio=0.50 exactly, >=0.50 → Low
    [InlineData(6_900_000_000L, PressureLevel.Low)]
    [InlineData(6_999_999_999L, PressureLevel.Low)]        // just below 0.70 boundary
    [InlineData(7_000_000_000L, PressureLevel.Medium)]     // ratio=0.70 exactly, >=0.70 → Medium
    [InlineData(8_400_000_000L, PressureLevel.Medium)]
    [InlineData(8_499_999_999L, PressureLevel.Medium)]     // just below 0.85 boundary
    [InlineData(8_500_000_000L, PressureLevel.High)]       // ratio=0.85 exactly, >=0.85 → High
    [InlineData(9_400_000_000L, PressureLevel.High)]
    [InlineData(9_499_999_999L, PressureLevel.High)]       // just below 0.95 boundary
    [InlineData(9_500_000_000L, PressureLevel.Critical)]   // ratio=0.95 exactly, >=0.95 → Critical
    [InlineData(10_000_000_000L, PressureLevel.Critical)]  // ratio=1.0, usage==budget
    public async Task ProfileAsync_PressureBoundaries(long usage, PressureLevel expectedPressure)
    {
        // Arrange — budget fixed at 10GB
        const long budget = 10_000_000_000L;
        var adapter = MakeAdapter(dedicated: budget, budget: budget, usage: usage);
        var provider = new FakeDxgiAdapterProvider(adapter);
        var profiler = new DxgiGpuProfiler(provider);

        // Act
        var result = await profiler.ProfileAsync();

        // Assert
        Assert.Equal(expectedPressure, result.GpuPressureLevel);
    }

    [Theory]
    // PartialOffload threshold: safe > 1GB (1,073,741,824). Flips when dedicated*0.85 > 1GB.
    [InlineData(1_200_000_000L, false, false)]  // safe=1,020,000,000 < 1GB
    [InlineData(1_300_000_000L, true, false)]   // safe=1,105,000,000 > 1GB
    // FullOffload threshold: safe > 4GB (4,294,967,296). Flips when dedicated*0.85 > 4GB.
    [InlineData(5_000_000_000L, true, false)]   // safe=4,250,000,000 < 4GB
    [InlineData(5_100_000_000L, true, true)]    // safe=4,335,000,000 > 4GB
    public async Task ProfileAsync_OffloadThresholds(long dedicated, bool expectedPartial, bool expectedFull)
    {
        // Arrange — budget=0 so avail=dedicated, safe=(long)(dedicated * 0.85)
        var adapter = MakeAdapter(dedicated: dedicated, budget: 0);
        var provider = new FakeDxgiAdapterProvider(adapter);
        var profiler = new DxgiGpuProfiler(provider);

        // Act
        var result = await profiler.ProfileAsync();

        // Assert
        Assert.Equal(expectedPartial, result.CanPartialOffload);
        Assert.Equal(expectedFull, result.CanFullOffload);
    }
}
