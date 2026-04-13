using Nexus.Hardware.Windows.Internals;

namespace Nexus.Hardware.Tests.Fakes;

internal sealed class FakePerfCounterProvider : IPerfCounterProvider
{
    private readonly float _cpuUsage;
    private readonly float _availableRamMb;
    private readonly float _pagesPerSecond;

    public bool Disposed { get; private set; }

    public FakePerfCounterProvider(float cpuUsage = 0f, float availableRamMb = 0f, float pagesPerSecond = 0f)
    {
        _cpuUsage = cpuUsage;
        _availableRamMb = availableRamMb;
        _pagesPerSecond = pagesPerSecond;
    }

    public float ReadCpuUsage() => _cpuUsage;

    public float ReadAvailableRamMb() => _availableRamMb;

    public float ReadPagesPerSecond() => _pagesPerSecond;

    public void Dispose() => Disposed = true;

    public static IPerfCounterProvider Throwing(Exception ex) => new ThrowingProvider(ex);

    private sealed class ThrowingProvider(Exception ex) : IPerfCounterProvider
    {
        public float ReadCpuUsage() => throw ex;
        public float ReadAvailableRamMb() => throw ex;
        public float ReadPagesPerSecond() => throw ex;
        public void Dispose() { }
    }
}
