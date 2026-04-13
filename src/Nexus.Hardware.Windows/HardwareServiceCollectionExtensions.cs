using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.Hardware.Abstractions;
using Nexus.Hardware.Windows.Internals;
using Nexus.Hardware.Windows.Monitoring;
using Nexus.Hardware.Windows.Profilers;

namespace Nexus.Hardware.Windows;

/// <summary>
/// Registers all Windows-specific hardware profiling and monitoring services into the DI container.
/// </summary>
[SupportedOSPlatform("windows")]
public static class HardwareServiceCollectionExtensions
{
    /// <summary>
    /// Adds Nexus hardware-intelligence services that depend on Windows APIs (WMI, DXGI, P/Invoke, LibreHardwareMonitor).
    /// </summary>
    /// <param name="services">The service collection to register hardware services into.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
    public static IServiceCollection AddNexusHardwareWindows(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Phase 1: Infrastructure — no dependencies
        services.AddSingleton<IWmiQuery>(sp => new WmiQueryService());
        services.AddSingleton<IDxgiAdapterProvider>(sp => new DxgiAdapterProvider());
        services.AddSingleton<ILhmComputer>(sp =>
            new LhmComputerWrapper(sp.GetService<ILogger<LhmComputerWrapper>>()));
        services.AddSingleton<IPerfCounterProvider>(sp =>
            new PerfCounterProvider(sp.GetService<ILogger<PerfCounterProvider>>()));

        // Phase 2: Profilers — depend on infrastructure
        services.AddSingleton<ICpuProfiler>(sp =>
            new WmiCpuProfiler(
                sp.GetRequiredService<IWmiQuery>(),
                sp.GetService<ILogger<WmiCpuProfiler>>()));
        services.AddSingleton<IGpuProfiler>(sp =>
            new DxgiGpuProfiler(
                sp.GetRequiredService<IDxgiAdapterProvider>(),
                sp.GetService<ILogger<DxgiGpuProfiler>>()));
        services.AddTransient<IRamProfiler>(sp =>
            new Win32RamProfiler(sp.GetService<ILogger<Win32RamProfiler>>()));

        // Phase 3: Composites — depend on profilers
        // Transient: each resolution gets a fresh Win32RamProfiler (also Transient).
        // Do NOT capture IHostProfiler in a Singleton — IRamProfiler is not safe to cache.
        services.AddTransient<IHostProfiler>(sp =>
            new WindowsHostProfiler(
                sp.GetRequiredService<ICpuProfiler>(),
                sp.GetRequiredService<IRamProfiler>(),
                sp.GetRequiredService<IGpuProfiler>(),
                sp.GetService<ILogger<WindowsHostProfiler>>()));

        // Phase 4: Monitoring — depend on infrastructure
        services.AddSingleton<ISensorMonitor>(sp =>
            new LhmSensorMonitor(
                sp.GetRequiredService<ILhmComputer>(),
                sp.GetService<ILogger<LhmSensorMonitor>>()));
        services.AddSingleton<PerfCounterMonitor>(sp =>
            new PerfCounterMonitor(
                sp.GetRequiredService<IPerfCounterProvider>(),
                sp.GetService<ILogger<PerfCounterMonitor>>()));

        return services;
    }
}
