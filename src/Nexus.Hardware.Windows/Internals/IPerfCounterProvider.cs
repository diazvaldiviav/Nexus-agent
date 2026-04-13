namespace Nexus.Hardware.Windows.Internals;

internal interface IPerfCounterProvider : IDisposable
{
    float ReadCpuUsage();
    float ReadAvailableRamMb();
    float ReadPagesPerSecond();
}
