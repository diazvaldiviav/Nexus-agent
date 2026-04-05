using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Nexus.Hardware.Abstractions;
using Nexus.Hardware.Envelopes;
using Nexus.Hardware.States;
using Nexus.Hardware.Windows.Internals;

namespace Nexus.Hardware.Windows.Profilers;

internal partial class Win32RamProfiler : IRamProfiler
{
    private const double SafeModelFraction = 0.70;
    private const double SafeInferenceFraction = 0.85;

    private readonly ILogger<Win32RamProfiler>? _logger;

    public Win32RamProfiler(ILogger<Win32RamProfiler>? logger = null)
    {
        _logger = logger;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    protected virtual MemoryStatusResult GetMemoryStatus()
    {
        var status = new MEMORYSTATUSEX();
        status.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();

        if (!GlobalMemoryStatusEx(ref status))
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        return new MemoryStatusResult(
            status.dwMemoryLoad,
            status.ullTotalPhys,
            status.ullAvailPhys,
            status.ullTotalPageFile,
            status.ullAvailPageFile);
    }

    public Task<RamEnvelope> ProfileAsync(CancellationToken ct = default)
    {
        try
        {
            var status = GetMemoryStatus();

            var usableRamNow = (long)status.AvailablePhysical;
            var safeModelRamBudget = (long)(usableRamNow * SafeModelFraction);
            var safeInferenceRamBudget = (long)(safeModelRamBudget * SafeInferenceFraction);

            var pressure = ClassifyPressure(status.MemoryLoadPercent);

            return Task.FromResult(new RamEnvelope(usableRamNow, safeModelRamBudget, safeInferenceRamBudget, pressure));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "RAM profiling failed, returning degraded envelope");
            return Task.FromResult(new RamEnvelope(0, 0, 0, PressureLevel.Critical));
        }
    }

    private static PressureLevel ClassifyPressure(uint memoryLoadPercent) => memoryLoadPercent switch
    {
        >= 95 => PressureLevel.Critical,
        >= 85 => PressureLevel.High,
        >= 70 => PressureLevel.Medium,
        >= 50 => PressureLevel.Low,
        _ => PressureLevel.None
    };
}
