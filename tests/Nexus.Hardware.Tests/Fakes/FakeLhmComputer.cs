using Nexus.Hardware.Windows.Internals;

namespace Nexus.Hardware.Tests.Fakes;

internal sealed class FakeLhmComputer : ILhmComputer, IDisposable
{
    private readonly bool _isOpen;
    private readonly IReadOnlyList<LhmSensorReading> _readings;

    public bool Disposed { get; private set; }

    public FakeLhmComputer(bool isOpen, params LhmSensorReading[] readings)
    {
        _isOpen = isOpen;
        _readings = readings;
    }

    public bool TryOpen() => _isOpen;

    public IReadOnlyList<LhmSensorReading> ReadSensors() => _readings;

    public void Dispose() => Disposed = true;

    public static FakeLhmComputer Unavailable() => new(false);

    public static ILhmComputer Throwing(Exception ex) => new ThrowingLhmComputer(ex);

    private sealed class ThrowingLhmComputer(Exception ex) : ILhmComputer, IDisposable
    {
        public bool TryOpen() => true;

        public IReadOnlyList<LhmSensorReading> ReadSensors() => throw ex;

        public void Dispose() { }
    }
}
