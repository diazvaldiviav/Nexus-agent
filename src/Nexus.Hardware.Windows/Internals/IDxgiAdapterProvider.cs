namespace Nexus.Hardware.Windows.Internals;

internal interface IDxgiAdapterProvider
{
    IReadOnlyList<DxgiAdapterInfo> GetAdapters();
}
