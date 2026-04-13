namespace Nexus.Hardware.Windows.Internals;

internal record DxgiAdapterInfo(
    string Description,
    uint VendorId,
    long DedicatedVideoMemory,
    long SharedSystemMemory,
    long LocalBudget,
    long LocalCurrentUsage,
    bool IsHardware
    );
