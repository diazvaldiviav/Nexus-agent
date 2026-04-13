namespace Nexus.Hardware.Windows.Internals;

internal interface ILhmComputer
{
    bool TryOpen();
    IReadOnlyList<LhmSensorReading> ReadSensors();
}
