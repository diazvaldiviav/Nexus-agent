using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace Nexus.Hardware.Windows.Internals;

[SupportedOSPlatform("windows")]
internal sealed class PerfCounterProvider : IPerfCounterProvider
{
    private readonly PerformanceCounter? _cpuCounter;
    private readonly PerformanceCounter? _ramCounter;
    private readonly PerformanceCounter? _pagesCounter;
    private readonly ILogger<PerfCounterProvider>? _logger;

    public PerfCounterProvider(ILogger<PerfCounterProvider>? logger = null)
    {
        _logger = logger;
        _cpuCounter = TryCreate("Processor", "% Processor Time", "_Total");
        _ramCounter = TryCreate("Memory", "Available MBytes");
        _pagesCounter = TryCreate("Memory", "Pages/sec");
    }

    public float ReadCpuUsage() => SafeRead(_cpuCounter);

    public float ReadAvailableRamMb() => SafeRead(_ramCounter);

    public float ReadPagesPerSecond() => SafeRead(_pagesCounter);

    public void Dispose()
    {
        _cpuCounter?.Dispose();
        _ramCounter?.Dispose();
        _pagesCounter?.Dispose();
    }

    private PerformanceCounter? TryCreate(string category, string counter, string? instance = null)
    {
        try
        {
            return instance is not null
                ? new PerformanceCounter(category, counter, instance)
                : new PerformanceCounter(category, counter);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to create PerformanceCounter: {Category}/{Counter}", category, counter);
            return null;
        }
    }

    private static float SafeRead(PerformanceCounter? counter)
    {
        try
        {
            return counter?.NextValue() ?? 0f;
        }
        catch (Exception)
        {
            return 0f;
        }
    }
}
