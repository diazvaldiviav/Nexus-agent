using Nexus.Hardware.Envelopes;
using Nexus.Hardware.States;

namespace Nexus.Hardware.Tests;

public class HostStateClassifierTests
{
    [Theory]
    [InlineData(0.0, CpuState.Weak)]
    [InlineData(0.24, CpuState.Weak)]
    [InlineData(0.25, CpuState.Moderate)]
    [InlineData(0.49, CpuState.Moderate)]
    [InlineData(0.50, CpuState.Strong)]
    [InlineData(0.74, CpuState.Strong)]
    [InlineData(0.75, CpuState.HighEnd)]
    [InlineData(1.0, CpuState.HighEnd)]
    public void ClassifyCpu_ReturnsExpectedState(double inferenceScore, CpuState expected)
    {
        // Arrange
        var envelope = MakeCpuEnvelope(inferenceScore);

        // Act
        var result = HostStateClassifier.ClassifyCpu(envelope);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(0L, RamState.Tight)]
    [InlineData(3_999_999_999L, RamState.Tight)]
    [InlineData(4_000_000_000L, RamState.Adequate)]
    [InlineData(7_999_999_999L, RamState.Adequate)]
    [InlineData(8_000_000_000L, RamState.Comfortable)]
    [InlineData(15_999_999_999L, RamState.Comfortable)]
    [InlineData(16_000_000_000L, RamState.Abundant)]
    [InlineData(32_000_000_000L, RamState.Abundant)]
    public void ClassifyRam_ReturnsExpectedState(long safeModelRamBudget, RamState expected)
    {
        // Arrange
        var envelope = MakeRamEnvelope(safeModelRamBudget);

        // Act
        var result = HostStateClassifier.ClassifyRam(envelope);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(-1L, GpuState.None)]
    [InlineData(0L, GpuState.None)]
    [InlineData(1L, GpuState.Limited)]
    [InlineData(3_999_999_999L, GpuState.Limited)]
    [InlineData(4_000_000_000L, GpuState.Capable)]
    [InlineData(7_999_999_999L, GpuState.Capable)]
    [InlineData(8_000_000_000L, GpuState.Strong)]
    [InlineData(16_000_000_000L, GpuState.Strong)]
    public void ClassifyGpu_ReturnsExpectedState(long safeGpuBudget, GpuState expected)
    {
        // Arrange
        var envelope = MakeGpuEnvelope(safeGpuBudget);

        // Act
        var result = HostStateClassifier.ClassifyGpu(envelope);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ClassifyCpu_NegativeScore_ReturnsWeak()
    {
        var envelope = MakeCpuEnvelope(-0.5);

        var result = HostStateClassifier.ClassifyCpu(envelope);

        Assert.Equal(CpuState.Weak, result);
    }

    [Fact]
    public void ClassifyRam_NegativeBudget_ReturnsTight()
    {
        var envelope = new RamEnvelope(8_000_000_000L, -1L, 4_000_000_000L, PressureLevel.High);

        var result = HostStateClassifier.ClassifyRam(envelope);

        Assert.Equal(RamState.Tight, result);
    }

    private static CpuEnvelope MakeCpuEnvelope(double inferenceScore) =>
        new(CpuArchitectureClass: "x86_64", CpuParallelismScore: 0.5, CpuSimdScore: 0.5,
            CpuInferenceScore: inferenceScore, MaxSafeCpuThreads: 4);

    private static RamEnvelope MakeRamEnvelope(long safeModelRamBudget) =>
        new(UsableRamNow: safeModelRamBudget * 2, SafeModelRamBudget: safeModelRamBudget,
            SafeInferenceRamBudget: safeModelRamBudget, RamPressureLevel: PressureLevel.Low);

    private static GpuEnvelope MakeGpuEnvelope(long safeGpuBudget) =>
        new(UsableLocalVramNow: safeGpuBudget, SafeGpuBudget: safeGpuBudget,
            GpuPressureLevel: PressureLevel.Low, GpuOffloadCapacity: safeGpuBudget,
            CanFullOffload: safeGpuBudget > 0, CanPartialOffload: safeGpuBudget > 0);
}
