using Nexus.Hardware.States;

namespace Nexus.Hardware.Tests;

public class EnumTests
{
    [Fact]
    public void CpuState_HasExpectedValues()
    {
        var values = Enum.GetValues<CpuState>();
        Assert.Equal(4, values.Length);
        Assert.True(Enum.IsDefined(CpuState.Weak));
        Assert.True(Enum.IsDefined(CpuState.Moderate));
        Assert.True(Enum.IsDefined(CpuState.Strong));
        Assert.True(Enum.IsDefined(CpuState.HighEnd));
    }

    [Fact]
    public void RamState_HasExpectedValues()
    {
        var values = Enum.GetValues<RamState>();
        Assert.Equal(4, values.Length);
        Assert.True(Enum.IsDefined(RamState.Tight));
        Assert.True(Enum.IsDefined(RamState.Adequate));
        Assert.True(Enum.IsDefined(RamState.Comfortable));
        Assert.True(Enum.IsDefined(RamState.Abundant));
    }

    [Fact]
    public void GpuState_HasExpectedValues()
    {
        var values = Enum.GetValues<GpuState>();
        Assert.Equal(4, values.Length);
        Assert.True(Enum.IsDefined(GpuState.None));
        Assert.True(Enum.IsDefined(GpuState.Limited));
        Assert.True(Enum.IsDefined(GpuState.Capable));
        Assert.True(Enum.IsDefined(GpuState.Strong));
    }

    [Fact]
    public void ArchitectureState_HasExpectedValues()
    {
        var values = Enum.GetValues<ArchitectureState>();
        Assert.Equal(4, values.Length);
        Assert.True(Enum.IsDefined(ArchitectureState.NativeOptimal));
        Assert.True(Enum.IsDefined(ArchitectureState.NativeCompatible));
        Assert.True(Enum.IsDefined(ArchitectureState.EmulatedPenalty));
        Assert.True(Enum.IsDefined(ArchitectureState.Unsupported));
    }

    [Fact]
    public void FeasibilityResult_HasExpectedValues()
    {
        var values = Enum.GetValues<FeasibilityResult>();
        Assert.Equal(4, values.Length);
        Assert.True(Enum.IsDefined(FeasibilityResult.Rejected));
        Assert.True(Enum.IsDefined(FeasibilityResult.FeasibleWithCaution));
        Assert.True(Enum.IsDefined(FeasibilityResult.Feasible));
        Assert.True(Enum.IsDefined(FeasibilityResult.Optimal));
    }

    [Fact]
    public void PlacementStrategy_HasExpectedValues()
    {
        var values = Enum.GetValues<PlacementStrategy>();
        Assert.Equal(4, values.Length);
        Assert.True(Enum.IsDefined(PlacementStrategy.CpuOnly));
        Assert.True(Enum.IsDefined(PlacementStrategy.GpuFull));
        Assert.True(Enum.IsDefined(PlacementStrategy.GpuPartial));
        Assert.True(Enum.IsDefined(PlacementStrategy.HybridFallback));
    }

    [Fact]
    public void SafetyLevel_HasExpectedValues()
    {
        var values = Enum.GetValues<SafetyLevel>();
        Assert.Equal(4, values.Length);
        Assert.True(Enum.IsDefined(SafetyLevel.Unsafe));
        Assert.True(Enum.IsDefined(SafetyLevel.Caution));
        Assert.True(Enum.IsDefined(SafetyLevel.Safe));
        Assert.True(Enum.IsDefined(SafetyLevel.Comfortable));
    }

    [Fact]
    public void PressureLevel_HasExpectedValues()
    {
        var values = Enum.GetValues<PressureLevel>();
        Assert.Equal(5, values.Length);
        Assert.True(Enum.IsDefined(PressureLevel.None));
        Assert.True(Enum.IsDefined(PressureLevel.Low));
        Assert.True(Enum.IsDefined(PressureLevel.Medium));
        Assert.True(Enum.IsDefined(PressureLevel.High));
        Assert.True(Enum.IsDefined(PressureLevel.Critical));
    }
}
