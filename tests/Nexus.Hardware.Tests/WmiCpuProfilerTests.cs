using System.Management;
using System.Runtime.InteropServices;
using Nexus.Hardware.Tests.Fakes;
using Nexus.Hardware.Windows.Profilers;

namespace Nexus.Hardware.Tests;

public class WmiCpuProfilerTests
{
    private static readonly double ActualSimdScore = WmiCpuProfiler.ComputeSimdScore();

    private static Dictionary<string, object> MakeRow(
        string name = "Test CPU",
        int cores = 8,
        int logicalCores = 16,
        int maxClockSpeed = 3600,
        ushort architecture = 9) => new()
    {
        ["Name"] = name,
        ["NumberOfCores"] = cores,
        ["NumberOfLogicalProcessors"] = logicalCores,
        ["MaxClockSpeed"] = maxClockSpeed,
        ["Architecture"] = architecture
    };

    [Fact]
    public async Task ProfileAsync_AmdRyzen9_ProducesCorrectEnvelope()
    {
        // Arrange
        var wmi = new FakeWmiQuery(MakeRow("AMD Ryzen 9 7950X", 16, 32, 4500, 9));
        var profiler = new WmiCpuProfiler(wmi);

        // Act
        var envelope = await profiler.ProfileAsync();

        // Assert
        Assert.Equal(30, envelope.MaxSafeCpuThreads);
        Assert.Equal(1.0, envelope.CpuParallelismScore, 3);
        var expectedClock = Math.Min(1.0, 4500.0 / 5000.0);
        var expectedInference = Math.Min(1.0, 1.0 * 0.4 + ActualSimdScore * 0.3 + expectedClock * 0.3);
        Assert.Equal(expectedInference, envelope.CpuInferenceScore, 3);
    }

    [Fact]
    public async Task ProfileAsync_IntelI7_ProducesCorrectEnvelope()
    {
        // Arrange
        var wmi = new FakeWmiQuery(MakeRow("Intel Core i7-13700K", 8, 16, 3600, 9));
        var profiler = new WmiCpuProfiler(wmi);

        // Act
        var envelope = await profiler.ProfileAsync();

        // Assert
        Assert.Equal(14, envelope.MaxSafeCpuThreads);
        Assert.Equal(1.0, envelope.CpuParallelismScore, 3);
        var expectedClock = Math.Min(1.0, 3600.0 / 5000.0);
        Assert.Equal(0.72, expectedClock, 3);
        var expectedInference = Math.Min(1.0, 1.0 * 0.4 + ActualSimdScore * 0.3 + expectedClock * 0.3);
        Assert.Equal(expectedInference, envelope.CpuInferenceScore, 3);
    }

    [Fact]
    public async Task ProfileAsync_SingleCore_MinimumOneThread()
    {
        // Arrange
        var wmi = new FakeWmiQuery(MakeRow("Atom N270", 1, 1, 2000, 9));
        var profiler = new WmiCpuProfiler(wmi);

        // Act
        var envelope = await profiler.ProfileAsync();

        // Assert
        Assert.Equal(1, envelope.MaxSafeCpuThreads);
    }

    [Fact]
    public async Task ProfileAsync_EmptyWmiResult_ReturnsDegradedEnvelope()
    {
        // Arrange
        var wmi = new FakeWmiQuery();
        var profiler = new WmiCpuProfiler(wmi);

        // Act
        var envelope = await profiler.ProfileAsync();

        // Assert
        Assert.Equal("Unknown", envelope.CpuArchitectureClass);
        Assert.Equal(0, envelope.CpuParallelismScore);
        Assert.Equal(0, envelope.CpuSimdScore);
        Assert.Equal(0, envelope.CpuInferenceScore);
        Assert.Equal(1, envelope.MaxSafeCpuThreads);
    }

    [Fact]
    public async Task ProfileAsync_WmiThrows_ReturnsDegradedEnvelope()
    {
        // Arrange
        var wmi = FakeWmiQuery.Throwing(new ManagementException());
        var profiler = new WmiCpuProfiler(wmi);

        // Act
        var envelope = await profiler.ProfileAsync();

        // Assert
        Assert.Equal("Unknown", envelope.CpuArchitectureClass);
        Assert.Equal(0, envelope.CpuParallelismScore);
        Assert.Equal(0, envelope.CpuSimdScore);
        Assert.Equal(0, envelope.CpuInferenceScore);
        Assert.Equal(1, envelope.MaxSafeCpuThreads);
    }

    [Theory]
    [InlineData((ushort)0, "x86")]
    [InlineData((ushort)5, "ARM")]
    [InlineData((ushort)9, "x64")]
    [InlineData((ushort)12, "ARM64")]
    public void MapArchitecture_KnownValues_MapsCorrectly(ushort wmiArch, string expected)
    {
        Assert.Equal(expected, WmiCpuProfiler.MapArchitecture(wmiArch));
    }

    [Fact]
    public void MapArchitecture_UnknownValue_ReturnsUnknown()
    {
        Assert.Equal("Unknown", WmiCpuProfiler.MapArchitecture(99));
    }

    [Fact]
    public async Task ProfileAsync_ScoresAreCapped()
    {
        // Arrange — extreme values that would exceed 1.0 without capping
        var wmi = new FakeWmiQuery(MakeRow("Threadripper", 32, 64, 6000, 9));
        var profiler = new WmiCpuProfiler(wmi);

        // Act
        var envelope = await profiler.ProfileAsync();

        // Assert
        Assert.True(envelope.CpuParallelismScore <= 1.0, "Parallelism score should be capped at 1.0");
        Assert.True(envelope.CpuSimdScore <= 1.0, "SIMD score should be capped at 1.0");
        Assert.True(envelope.CpuInferenceScore <= 1.0, "Inference score should be capped at 1.0");
    }

    [Fact]
    public void ComputeSimdScore_ReturnsValueInRange()
    {
        var score = WmiCpuProfiler.ComputeSimdScore();

        Assert.InRange(score, 0.10, 1.0);
    }

    [Fact]
    public async Task ProfileAsync_ComException_ReturnsDegradedEnvelope()
    {
        // Arrange
        var wmi = FakeWmiQuery.Throwing(new COMException("RPC server unavailable"));
        var profiler = new WmiCpuProfiler(wmi);

        // Act
        var envelope = await profiler.ProfileAsync();

        // Assert
        Assert.Equal("Unknown", envelope.CpuArchitectureClass);
        Assert.Equal(0, envelope.CpuParallelismScore);
        Assert.Equal(0, envelope.CpuSimdScore);
        Assert.Equal(0, envelope.CpuInferenceScore);
        Assert.Equal(1, envelope.MaxSafeCpuThreads);
    }

    [Fact]
    public void Constructor_NullWmiQuery_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new WmiCpuProfiler(null!));
    }
}
