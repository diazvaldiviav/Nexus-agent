using System.Runtime.InteropServices;
using Nexus.Hardware.Abstractions;
using Nexus.Hardware.Envelopes;
using Nexus.Hardware.States;
using Nexus.Hardware.Windows.Profilers;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Nexus.Hardware.Tests;

public class WindowsHostProfilerTests
{
    private static readonly CpuEnvelope ValidCpu = new("x64", 0.8, 0.75, 0.70, 14);
    private static readonly RamEnvelope ValidRam = new(20_000_000_000L, 14_000_000_000L, 11_900_000_000L, PressureLevel.None);
    private static readonly GpuEnvelope ValidGpu = new(20_000_000_000L, 17_000_000_000L, PressureLevel.Low, 17_000_000_000L, true, true);

    private static (ICpuProfiler cpu, IRamProfiler ram, IGpuProfiler gpu) CreateMocks()
    {
        var cpu = Substitute.For<ICpuProfiler>();
        var ram = Substitute.For<IRamProfiler>();
        var gpu = Substitute.For<IGpuProfiler>();
        return (cpu, ram, gpu);
    }

    private static void SetupAllValid(ICpuProfiler cpu, IRamProfiler ram, IGpuProfiler gpu)
    {
        cpu.ProfileAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(ValidCpu));
        ram.ProfileAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(ValidRam));
        gpu.ProfileAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(ValidGpu));
    }

    [Fact]
    public async Task BuildProfileAsync_AllSucceed_CompleteProfile()
    {
        // Arrange
        var (cpu, ram, gpu) = CreateMocks();
        SetupAllValid(cpu, ram, gpu);
        var profiler = new WindowsHostProfiler(cpu, ram, gpu);

        // Act
        var profile = await profiler.BuildProfileAsync();

        // Assert
        Assert.Equal(ValidCpu, profile.Cpu);
        Assert.Equal(ValidRam, profile.Ram);
        Assert.Equal(ValidGpu, profile.Gpu);
        Assert.Equal(CpuState.Strong, profile.CpuState);       // score 0.70 → <0.75 → Strong
        Assert.Equal(RamState.Comfortable, profile.RamState); // budget 14GB → <16GB → Comfortable
        Assert.Equal(GpuState.Strong, profile.GpuState);      // budget 17GB → >=8GB → Strong
        Assert.False(string.IsNullOrEmpty(profile.OsVersion));
    }

    [Fact]
    public async Task BuildProfileAsync_CpuFails_DegradedCpu()
    {
        // Arrange
        var (cpu, ram, gpu) = CreateMocks();
        cpu.ProfileAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("fail"));
        ram.ProfileAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(ValidRam));
        gpu.ProfileAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(ValidGpu));
        var profiler = new WindowsHostProfiler(cpu, ram, gpu);

        // Act
        var profile = await profiler.BuildProfileAsync();

        // Assert
        Assert.Equal(new CpuEnvelope("Unknown", 0, 0, 0, 1), profile.Cpu);
        Assert.Equal(CpuState.Weak, profile.CpuState);
        Assert.Equal(ValidRam, profile.Ram);
        Assert.Equal(ValidGpu, profile.Gpu);
    }

    [Fact]
    public async Task BuildProfileAsync_RamFails_DegradedRam()
    {
        // Arrange
        var (cpu, ram, gpu) = CreateMocks();
        cpu.ProfileAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(ValidCpu));
        ram.ProfileAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("fail"));
        gpu.ProfileAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(ValidGpu));
        var profiler = new WindowsHostProfiler(cpu, ram, gpu);

        // Act
        var profile = await profiler.BuildProfileAsync();

        // Assert
        Assert.Equal(new RamEnvelope(0, 0, 0, PressureLevel.Critical), profile.Ram);
        Assert.Equal(RamState.Tight, profile.RamState);
        Assert.Equal(ValidCpu, profile.Cpu);
        Assert.Equal(ValidGpu, profile.Gpu);
    }

    [Fact]
    public async Task BuildProfileAsync_GpuFails_NoGpu()
    {
        // Arrange
        var (cpu, ram, gpu) = CreateMocks();
        cpu.ProfileAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(ValidCpu));
        ram.ProfileAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(ValidRam));
        gpu.ProfileAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("fail"));
        var profiler = new WindowsHostProfiler(cpu, ram, gpu);

        // Act
        var profile = await profiler.BuildProfileAsync();

        // Assert
        Assert.Equal(GpuEnvelope.NoGpu(), profile.Gpu);
        Assert.Equal(GpuState.None, profile.GpuState);
        Assert.Equal(ValidCpu, profile.Cpu);
        Assert.Equal(ValidRam, profile.Ram);
    }

    [Fact]
    public async Task BuildProfileAsync_AllFail_DegradedProfile()
    {
        // Arrange
        var (cpu, ram, gpu) = CreateMocks();
        cpu.ProfileAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("fail"));
        ram.ProfileAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("fail"));
        gpu.ProfileAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("fail"));
        var profiler = new WindowsHostProfiler(cpu, ram, gpu);

        // Act
        var profile = await profiler.BuildProfileAsync();

        // Assert
        Assert.Equal(new CpuEnvelope("Unknown", 0, 0, 0, 1), profile.Cpu);
        Assert.Equal(new RamEnvelope(0, 0, 0, PressureLevel.Critical), profile.Ram);
        Assert.Equal(GpuEnvelope.NoGpu(), profile.Gpu);
        Assert.Equal(CpuState.Weak, profile.CpuState);
        Assert.Equal(RamState.Tight, profile.RamState);
        Assert.Equal(GpuState.None, profile.GpuState);
    }

    [Theory]
    [InlineData(Architecture.X64, Architecture.X64, ArchitectureState.NativeOptimal)]
    [InlineData(Architecture.Arm64, Architecture.Arm64, ArchitectureState.NativeOptimal)]
    [InlineData(Architecture.Arm64, Architecture.X64, ArchitectureState.EmulatedPenalty)]
    [InlineData(Architecture.X64, Architecture.X86, ArchitectureState.NativeCompatible)]
    [InlineData(Architecture.Arm64, Architecture.X86, ArchitectureState.Unsupported)]
    public void ClassifyArchitecture_Theory(Architecture osArch, Architecture procArch, ArchitectureState expected)
    {
        // Act
        var result = WindowsHostProfiler.ClassifyArchitecture(osArch, procArch);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task BuildProfileAsync_ProfiledAtIsRecent()
    {
        // Arrange
        var (cpu, ram, gpu) = CreateMocks();
        SetupAllValid(cpu, ram, gpu);
        var profiler = new WindowsHostProfiler(cpu, ram, gpu);

        // Act
        var profile = await profiler.BuildProfileAsync();

        // Assert
        Assert.True((DateTime.UtcNow - profile.ProfiledAt).TotalSeconds < 5);
    }
}
