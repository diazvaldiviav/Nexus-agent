using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Hardware.Abstractions;
using Xunit.Abstractions;
using Nexus.Hardware.Windows;
using Nexus.Hardware.Windows.Internals;
using Nexus.Hardware.Windows.Monitoring;
using Nexus.Hardware.Windows.Profilers;

namespace Nexus.Hardware.Tests;

[Trait("Category", "Integration")]
[SupportedOSPlatform("windows")]
public class HardwareDiRegistrationTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly ITestOutputHelper _output;

    public HardwareDiRegistrationTests(ITestOutputHelper output)
    {
        _output = output;
        var services = new ServiceCollection();
        services.AddNexusHardwareWindows();
        _provider = services.BuildServiceProvider();
    }

    [Fact]
    public void Resolve_ICpuProfiler_ReturnsWmiCpuProfiler()
    {
        var profiler = _provider.GetRequiredService<ICpuProfiler>();
        Assert.IsType<WmiCpuProfiler>(profiler);
    }

    [Fact]
    public void Resolve_IRamProfiler_ReturnsWin32RamProfiler()
    {
        var profiler = _provider.GetRequiredService<IRamProfiler>();
        Assert.IsType<Win32RamProfiler>(profiler);
    }

    [Fact]
    public void Resolve_IGpuProfiler_ReturnsDxgiGpuProfiler()
    {
        var profiler = _provider.GetRequiredService<IGpuProfiler>();
        Assert.IsType<DxgiGpuProfiler>(profiler);
    }

    [Fact]
    public void Resolve_IHostProfiler_ReturnsWindowsHostProfiler()
    {
        var profiler = _provider.GetRequiredService<IHostProfiler>();
        Assert.IsType<WindowsHostProfiler>(profiler);
    }

    [Fact]
    public void Resolve_ISensorMonitor_ReturnsLhmSensorMonitor()
    {
        var monitor = _provider.GetRequiredService<ISensorMonitor>();
        Assert.IsType<LhmSensorMonitor>(monitor);
    }

    [Fact]
    public void Resolve_PerfCounterMonitor_ReturnsInstance()
    {
        var monitor = _provider.GetRequiredService<PerfCounterMonitor>();
        Assert.IsType<PerfCounterMonitor>(monitor);
    }

    [Fact]
    public void IRamProfiler_IsTransient_ProducesDifferentInstances()
    {
        var first = _provider.GetRequiredService<IRamProfiler>();
        var second = _provider.GetRequiredService<IRamProfiler>();
        Assert.NotSame(first, second);
    }

    [Fact]
    public void ICpuProfiler_IsSingleton_ReturnsSameInstance()
    {
        var first = _provider.GetRequiredService<ICpuProfiler>();
        var second = _provider.GetRequiredService<ICpuProfiler>();
        Assert.Same(first, second);
    }

    [Fact]
    public void IHostProfiler_IsTransient_ProducesDifferentInstances()
    {
        var first = _provider.GetRequiredService<IHostProfiler>();
        var second = _provider.GetRequiredService<IHostProfiler>();
        Assert.NotSame(first, second);
    }

    [Fact]
    public void ISensorMonitor_IsSingleton_ReturnsSameInstance()
    {
        var first = _provider.GetRequiredService<ISensorMonitor>();
        var second = _provider.GetRequiredService<ISensorMonitor>();
        Assert.Same(first, second);
    }

    [Fact]
    public void Diagnostic_DxgiAdapters_ListAll()
    {
        var adapterProvider = _provider.GetRequiredService<IDxgiAdapterProvider>();
        var adapters = adapterProvider.GetAdapters();

        _output.WriteLine($"DXGI Adapters found: {adapters.Count}");
        for (int i = 0; i < adapters.Count; i++)
        {
            var a = adapters[i];
            _output.WriteLine($"  [{i}] {a.Description}");
            _output.WriteLine($"      VendorId=0x{a.VendorId:X4}, DedicatedVRAM={a.DedicatedVideoMemory / 1_073_741_824.0:F1} GB, SharedMem={a.SharedSystemMemory / 1_073_741_824.0:F1} GB");
            _output.WriteLine($"      LocalBudget={a.LocalBudget / 1_073_741_824.0:F1} GB, CurrentUsage={a.LocalCurrentUsage / 1_073_741_824.0:F1} GB, IsHardware={a.IsHardware}");
        }

        if (adapters.Count == 0)
            _output.WriteLine("WARNING: No adapters found — DXGI enumeration returned empty");
    }

    [Fact]
    public async Task SmokeTest_BuildProfileAsync_PrintsFullProfile()
    {
        var profiler = _provider.GetRequiredService<IHostProfiler>();
        var profile = await profiler.BuildProfileAsync();

        _output.WriteLine("=== Host Capability Profile ===");
        _output.WriteLine($"CPU: {profile.Cpu.CpuArchitectureClass}, InferenceScore={profile.Cpu.CpuInferenceScore:F3}, SIMD={profile.Cpu.CpuSimdScore:F2}, Parallelism={profile.Cpu.CpuParallelismScore:F2}, MaxThreads={profile.Cpu.MaxSafeCpuThreads}");
        _output.WriteLine($"RAM: Available={profile.Ram.UsableRamNow / 1_073_741_824.0:F1} GB, ModelBudget={profile.Ram.SafeModelRamBudget / 1_073_741_824.0:F1} GB, InferenceBudget={profile.Ram.SafeInferenceRamBudget / 1_073_741_824.0:F1} GB, Pressure={profile.Ram.RamPressureLevel}");
        _output.WriteLine($"GPU: VRAM={profile.Gpu.UsableLocalVramNow / 1_073_741_824.0:F1} GB, Budget={profile.Gpu.SafeGpuBudget / 1_073_741_824.0:F1} GB, FullOffload={profile.Gpu.CanFullOffload}, PartialOffload={profile.Gpu.CanPartialOffload}, Pressure={profile.Gpu.GpuPressureLevel}");
        _output.WriteLine($"States: CPU={profile.CpuState}, RAM={profile.RamState}, GPU={profile.GpuState}, Arch={profile.ArchitectureState}");
        _output.WriteLine($"OS: {profile.OsVersion}, Arch={profile.OsArchitecture}, Process={profile.ProcessArchitecture}");
        _output.WriteLine($"Profiled at: {profile.ProfiledAt:O}");

        Assert.True(profile.Cpu.IsViable(), "CPU should be viable");
        Assert.True(profile.Ram.IsViable(), "RAM should be viable");
    }

    public void Dispose() => _provider.Dispose();
}
