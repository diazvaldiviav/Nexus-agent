using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Nexus.Hardware.Abstractions;
using Nexus.Hardware.Envelopes;
using Nexus.Hardware.States;

namespace Nexus.Hardware.Windows.Profilers;

/// <summary>
/// Orchestrates concurrent CPU, RAM, and GPU profiling on Windows, classifying each subsystem
/// into discrete capability states and producing a complete <see cref="HostCapabilityProfile"/>.
/// Individual profiler failures are isolated — the remaining subsystems still report valid data.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsHostProfiler : IHostProfiler
{
    private readonly ICpuProfiler _cpuProfiler;
    private readonly IRamProfiler _ramProfiler;
    private readonly IGpuProfiler _gpuProfiler;
    private readonly ILogger<WindowsHostProfiler>? _logger;

    private static readonly CpuEnvelope CpuFallback = new("Unknown", 0, 0, 0, 1);
    private static readonly RamEnvelope RamFallback = new(0, 0, 0, PressureLevel.Critical);
    private static readonly GpuEnvelope GpuFallback = GpuEnvelope.NoGpu();

    public WindowsHostProfiler(
        ICpuProfiler cpuProfiler,
        IRamProfiler ramProfiler,
        IGpuProfiler gpuProfiler,
        ILogger<WindowsHostProfiler>? logger = null)
    {
        _cpuProfiler = cpuProfiler ?? throw new ArgumentNullException(nameof(cpuProfiler));
        _ramProfiler = ramProfiler ?? throw new ArgumentNullException(nameof(ramProfiler));
        _gpuProfiler = gpuProfiler ?? throw new ArgumentNullException(nameof(gpuProfiler));
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<HostCapabilityProfile> BuildProfileAsync(CancellationToken ct = default)
    {
        var cpuTask = ProfileSafe(_cpuProfiler.ProfileAsync(ct), CpuFallback, "CPU");
        var ramTask = ProfileSafe(_ramProfiler.ProfileAsync(ct), RamFallback, "RAM");
        var gpuTask = ProfileSafe(_gpuProfiler.ProfileAsync(ct), GpuFallback, "GPU");

        await Task.WhenAll(cpuTask, ramTask, gpuTask).ConfigureAwait(false);

        var cpu = await cpuTask.ConfigureAwait(false);
        var ram = await ramTask.ConfigureAwait(false);
        var gpu = await gpuTask.ConfigureAwait(false);

        var cpuState = HostStateClassifier.ClassifyCpu(cpu);
        var ramState = HostStateClassifier.ClassifyRam(ram);
        var gpuState = HostStateClassifier.ClassifyGpu(gpu);
        var archState = ClassifyArchitecture(
            RuntimeInformation.OSArchitecture,
            RuntimeInformation.ProcessArchitecture);

        return new HostCapabilityProfile(
            cpu,
            ram,
            gpu,
            cpuState,
            ramState,
            gpuState,
            archState,
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture,
            RuntimeInformation.ProcessArchitecture,
            DateTime.UtcNow);
    }

    private async Task<T> ProfileSafe<T>(Task<T> profileTask, T fallback, string subsystem)
    {
        try
        {
            return await profileTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "{Subsystem} profiling failed, using fallback", subsystem);
            return fallback;
        }
    }

    internal static ArchitectureState ClassifyArchitecture(Architecture osArch, Architecture procArch)
    {
        if (osArch == procArch)
            return ArchitectureState.NativeOptimal;

        return (osArch, procArch) switch
        {
            (Architecture.Arm64, Architecture.X64) => ArchitectureState.EmulatedPenalty,
            (Architecture.X64, Architecture.X86) => ArchitectureState.NativeCompatible,
            _ => ArchitectureState.Unsupported
        };
    }
}
