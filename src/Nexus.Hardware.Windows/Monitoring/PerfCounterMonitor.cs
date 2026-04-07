using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Nexus.Hardware.Monitoring;
using Nexus.Hardware.Windows.Internals;

namespace Nexus.Hardware.Windows.Monitoring;

[SupportedOSPlatform("windows")]
internal sealed class PerfCounterMonitor : IDisposable
{
    private readonly IPerfCounterProvider _provider;
    private readonly ILogger<PerfCounterMonitor>? _logger;

    public PerfCounterMonitor(IPerfCounterProvider provider, ILogger<PerfCounterMonitor>? logger = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _logger = logger;
    }

    public SystemHealthSnapshot ReadSnapshot()
    {
        float cpuUsage;
        try
        {
            cpuUsage = _provider.ReadCpuUsage();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to read CPU usage from performance counter");
            cpuUsage = 0f;
        }

        float availableRamMb;
        try
        {
            availableRamMb = _provider.ReadAvailableRamMb();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to read available RAM from performance counter");
            availableRamMb = 0f;
        }

        float pagesPerSecond;
        try
        {
            pagesPerSecond = _provider.ReadPagesPerSecond();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to read pages/sec from performance counter");
            pagesPerSecond = 0f;
        }

        return new SystemHealthSnapshot(cpuUsage, availableRamMb, pagesPerSecond, DateTime.UtcNow);
    }

    public void Dispose()
    {
        _provider.Dispose();
    }
}
