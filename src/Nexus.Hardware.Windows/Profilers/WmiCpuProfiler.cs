using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using Microsoft.Extensions.Logging;
using Nexus.Hardware.Abstractions;
using Nexus.Hardware.Envelopes;
using Nexus.Hardware.Windows.Internals;

namespace Nexus.Hardware.Windows.Profilers;

internal sealed class WmiCpuProfiler : ICpuProfiler
{
    private const string WmiQueryText = "SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, Architecture FROM Win32_Processor";
    private const double MaxClockSpeedMhzBaseline = 5000.0;
    private const double LogicalCoresBaseline = 16.0;
    private const int ReservedOsThreads = 2;

    private readonly IWmiQuery _wmiQuery;
    private readonly ILogger<WmiCpuProfiler>? _logger;

    public WmiCpuProfiler(IWmiQuery wmiQuery, ILogger<WmiCpuProfiler>? logger = null)
    {
        _wmiQuery = wmiQuery ?? throw new ArgumentNullException(nameof(wmiQuery));
        _logger = logger;
    }

    public async Task<CpuEnvelope> ProfileAsync(CancellationToken ct = default)
    {
        try
        {
            return await Task.Run(ProfileCore, ct).ConfigureAwait(false);
        }
        catch (System.Management.ManagementException ex)
        {
            _logger?.LogWarning(ex, "WMI query failed, returning degraded CPU envelope");
            return new CpuEnvelope("Unknown", 0, 0, 0, 1);
        }
        catch (COMException ex)
        {
            _logger?.LogWarning(ex, "COM error during WMI query, returning degraded CPU envelope");
            return new CpuEnvelope("Unknown", 0, 0, 0, 1);
        }
    }

    private CpuEnvelope ProfileCore()
    {
        var rows = _wmiQuery.Query(WmiQueryText);
        if (rows.Count == 0)
            return new CpuEnvelope("Unknown", 0, 0, 0, 1);

        var row = rows[0];
        var name = row.TryGetValue("Name", out var n) ? n?.ToString() ?? "Unknown" : "Unknown";
        var cores = row.TryGetValue("NumberOfCores", out var c) ? Convert.ToInt32(c) : 0;
        var logicalCores = row.TryGetValue("NumberOfLogicalProcessors", out var lc) ? Convert.ToInt32(lc) : 0;
        var maxClockSpeedMhz = row.TryGetValue("MaxClockSpeed", out var mcs) ? Convert.ToInt32(mcs) : 0;
        var wmiArch = row.TryGetValue("Architecture", out var a) ? Convert.ToUInt16(a) : (ushort)0;

        var archClass = ResolveArchitectureClass(wmiArch);
        var maxSafeCpuThreads = Math.Max(1, logicalCores - ReservedOsThreads);
        var parallelismScore = Math.Min(1.0, logicalCores / LogicalCoresBaseline);
        var simdScore = ComputeSimdScore();
        var clockNormalized = Math.Min(1.0, maxClockSpeedMhz / MaxClockSpeedMhzBaseline);
        var inferenceScore = Math.Min(1.0, parallelismScore * 0.4 + simdScore * 0.3 + clockNormalized * 0.3);

        return new CpuEnvelope(archClass, parallelismScore, simdScore, inferenceScore, maxSafeCpuThreads);
    }

    internal static string ResolveArchitectureClass(ushort wmiArch)
    {
        var hostArch = RuntimeInformation.OSArchitecture;
        var processArch = RuntimeInformation.ProcessArchitecture;

        var wmiArchStr = MapArchitecture(wmiArch);

        if (hostArch != processArch)
            return $"{MapRuntimeArch(hostArch)} (emulated {MapRuntimeArch(processArch)})";

        if (wmiArchStr != "Unknown")
            return wmiArchStr;

        return MapRuntimeArch(hostArch);
    }

    internal static string MapArchitecture(ushort wmiArch) => wmiArch switch
    {
        0 => "x86",
        5 => "ARM",
        9 => "x64",
        12 => "ARM64",
        _ => "Unknown"
    };

    internal static double ComputeSimdScore()
    {
        double score = 0.10;
        if (Sse42.IsSupported) score = 0.30;
        if (Avx.IsSupported) score = 0.50;
        if (Avx2.IsSupported) score = 0.75;
        if (Avx512F.IsSupported) score = 1.00;
        if (AdvSimd.IsSupported && score < 0.60) score = 0.60;
        return score;
    }

    private static string MapRuntimeArch(Architecture arch) => arch switch
    {
        Architecture.X86 => "x86",
        Architecture.X64 => "x64",
        Architecture.Arm => "ARM",
        Architecture.Arm64 => "ARM64",
        _ => "Unknown"
    };
}
