namespace Nexus.Hardware.Windows.Internals;

internal record MemoryStatusResult(
    uint MemoryLoadPercent,
    ulong TotalPhysical,
    ulong AvailablePhysical,
    ulong TotalPageFile,
    ulong AvailablePageFile);
