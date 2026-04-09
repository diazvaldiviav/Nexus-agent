using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Nexus.Hardware.Abstractions;
using Nexus.Hardware.Envelopes;
using Nexus.Hardware.States;
using Nexus.Hardware.Windows.Internals;

namespace Nexus.Hardware.Windows.Profilers;

[SupportedOSPlatform("windows")]
internal sealed class DxgiGpuProfiler : IGpuProfiler
{
    private const long FourGigabytes = 4L * 1024 * 1024 * 1024;
    private const long OneGigabyte = 1L * 1024 * 1024 * 1024;
    private const double BudgetSafetyMultiplier = 0.85;

    private readonly IDxgiAdapterProvider _adapterProvider;
    private readonly ILogger<DxgiGpuProfiler>? _logger;

    public DxgiGpuProfiler(IDxgiAdapterProvider adapterProvider, ILogger<DxgiGpuProfiler>? logger = null)
    {
        _adapterProvider = adapterProvider ?? throw new ArgumentNullException(nameof(adapterProvider));
        _logger = logger;
    }

    public Task<GpuEnvelope> ProfileAsync(CancellationToken ct = default)
    {
        try
        {
            var adapters = _adapterProvider.GetAdapters();

            if (adapters.Count == 0)
                return Task.FromResult(GpuEnvelope.NoGpu());

            int bestIndex = 0;
            long maxDedicatedVram = adapters[0].DedicatedVideoMemory;

            for (int i = 1; i < adapters.Count; i++)
            {
                if (adapters[i].DedicatedVideoMemory > maxDedicatedVram)
                {
                    maxDedicatedVram = adapters[i].DedicatedVideoMemory;
                    bestIndex = i;
                }
            }

            var best = adapters[bestIndex];
            var dedicatedVideoMemory = best.DedicatedVideoMemory;
            var localBudget = best.LocalBudget;
            var currentUsage = best.LocalCurrentUsage;

            var availableVram = (localBudget > 0)
                ? Math.Max(0, localBudget - currentUsage)
                : dedicatedVideoMemory;

            var safeGpuBudget = (long)(availableVram * BudgetSafetyMultiplier);
            var canFullOffload = safeGpuBudget > FourGigabytes;
            var canPartialOffload = safeGpuBudget > OneGigabyte;
            var pressure = ClassifyGpuPressure(localBudget, currentUsage);

            return Task.FromResult(new GpuEnvelope(
                availableVram,
                safeGpuBudget,
                pressure,
                safeGpuBudget,
                canFullOffload,
                canPartialOffload));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "GPU profiling failed, returning no-GPU envelope");
            return Task.FromResult(GpuEnvelope.NoGpu());
        }
    }

    private static PressureLevel ClassifyGpuPressure(long localBudget, long currentUsage)
    {
        if (localBudget <= 0)
            return PressureLevel.None;

        var ratio = (double)currentUsage / localBudget;

        return ratio switch
        {
            >= 0.95 => PressureLevel.Critical,
            >= 0.85 => PressureLevel.High,
            >= 0.70 => PressureLevel.Medium,
            >= 0.50 => PressureLevel.Low,
            _ => PressureLevel.None
        };
    }
}
