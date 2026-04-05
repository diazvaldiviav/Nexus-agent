using Nexus.Hardware.Windows.Internals;
using Nexus.Hardware.Windows.Profilers;

namespace Nexus.Hardware.Tests.Fakes;

internal sealed class TestableRamProfiler : Win32RamProfiler
{
    private readonly MemoryStatusResult? _result;
    private readonly Exception? _exception;

    public TestableRamProfiler(MemoryStatusResult result) : base(null)
    {
        _result = result;
    }

    private TestableRamProfiler(Exception exception) : base(null)
    {
        _exception = exception;
    }

    protected override MemoryStatusResult GetMemoryStatus()
    {
        if (_exception is not null)
            throw _exception;

        return _result!;
    }

    public static TestableRamProfiler Throwing(Exception ex) => new(ex);
}
