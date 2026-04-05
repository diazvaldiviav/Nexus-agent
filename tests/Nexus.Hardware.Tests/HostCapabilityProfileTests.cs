using System.Runtime.InteropServices;
using System.Text.Json;
using Nexus.Hardware.Envelopes;
using Nexus.Hardware.States;

namespace Nexus.Hardware.Tests;

public class HostCapabilityProfileTests
{
    private static HostCapabilityProfile CreateSampleProfile()
    {
        var cpu = new CpuEnvelope("x86_64", 12.5, 8.0, 6.3, 16);
        var ram = new RamEnvelope(32_000_000_000L, 24_000_000_000L, 20_000_000_000L, PressureLevel.Low);
        var gpu = new GpuEnvelope(8_000_000_000L, 6_000_000_000L, PressureLevel.Low, 4_000_000_000L, true, true);

        return new HostCapabilityProfile(
            Cpu: cpu,
            Ram: ram,
            Gpu: gpu,
            CpuState: CpuState.Strong,
            RamState: RamState.Comfortable,
            GpuState: GpuState.Capable,
            ArchitectureState: ArchitectureState.NativeOptimal,
            OsVersion: "Windows 11 10.0.26200",
            OsArchitecture: Architecture.X64,
            ProcessArchitecture: Architecture.X64,
            ProfiledAt: new DateTime(2026, 4, 4, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var profile = CreateSampleProfile();

        Assert.Equal("x86_64", profile.Cpu.CpuArchitectureClass);
        Assert.Equal(12.5, profile.Cpu.CpuParallelismScore);
        Assert.Equal(32_000_000_000L, profile.Ram.UsableRamNow);
        Assert.Equal(24_000_000_000L, profile.Ram.SafeModelRamBudget);
        Assert.Equal(8_000_000_000L, profile.Gpu.UsableLocalVramNow);
        Assert.True(profile.Gpu.CanFullOffload);
        Assert.Equal(CpuState.Strong, profile.CpuState);
        Assert.Equal(RamState.Comfortable, profile.RamState);
        Assert.Equal(GpuState.Capable, profile.GpuState);
        Assert.Equal(ArchitectureState.NativeOptimal, profile.ArchitectureState);
        Assert.Equal("Windows 11 10.0.26200", profile.OsVersion);
        Assert.Equal(Architecture.X64, profile.OsArchitecture);
        Assert.Equal(Architecture.X64, profile.ProcessArchitecture);
        Assert.Equal(new DateTime(2026, 4, 4, 12, 0, 0, DateTimeKind.Utc), profile.ProfiledAt);
    }

    [Fact]
    public void UsesArchitectureFromInteropServices()
    {
        var profile = CreateSampleProfile() with
        {
            OsArchitecture = Architecture.Arm64,
            ProcessArchitecture = Architecture.Arm64
        };

        Assert.Equal(Architecture.Arm64, profile.OsArchitecture);
        Assert.Equal(Architecture.Arm64, profile.ProcessArchitecture);
    }

    [Fact]
    public void WithExpression_CreatesModifiedCopy()
    {
        var original = CreateSampleProfile();

        var modified = original with { OsVersion = "Ubuntu 24.04" };

        Assert.Equal("Windows 11 10.0.26200", original.OsVersion);
        Assert.Equal("Ubuntu 24.04", modified.OsVersion);
        Assert.Equal(original.Cpu, modified.Cpu);
        Assert.Equal(original.Ram, modified.Ram);
        Assert.Equal(original.ProfiledAt, modified.ProfiledAt);
    }

    [Fact]
    public void StructuralEquality_EqualValues_AreEqual()
    {
        var a = CreateSampleProfile();
        var b = CreateSampleProfile();

        Assert.Equal(a, b);
    }

    [Fact]
    public void StructuralEquality_DifferentValues_NotEqual()
    {
        var a = CreateSampleProfile();
        var b = CreateSampleProfile() with { CpuState = CpuState.Weak };

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void JsonRoundTrip_PreservesAllValues()
    {
        var original = CreateSampleProfile();

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<HostCapabilityProfile>(json);

        Assert.Equal(original, deserialized);
        Assert.Equal(DateTimeKind.Utc, deserialized!.ProfiledAt.Kind);
    }

    [Fact]
    public void JsonRoundTrip_WithNoGpu_PreservesValues()
    {
        var profile = CreateSampleProfile() with { Gpu = GpuEnvelope.NoGpu(), GpuState = GpuState.None };

        var json = JsonSerializer.Serialize(profile);
        var deserialized = JsonSerializer.Deserialize<HostCapabilityProfile>(json);

        Assert.Equal(profile, deserialized);
        Assert.Equal(DateTimeKind.Utc, deserialized!.ProfiledAt.Kind);
        Assert.Equal(0L, deserialized.Gpu.UsableLocalVramNow);
        Assert.False(deserialized.Gpu.CanFullOffload);
        Assert.Equal(GpuState.None, deserialized.GpuState);
    }
}
