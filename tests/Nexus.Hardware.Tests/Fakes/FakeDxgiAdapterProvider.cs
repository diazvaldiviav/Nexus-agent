using Nexus.Hardware.Windows.Internals;

namespace Nexus.Hardware.Tests.Fakes;

internal sealed class FakeDxgiAdapterProvider : IDxgiAdapterProvider
{
    private readonly IReadOnlyList<DxgiAdapterInfo> _adapters;

    public FakeDxgiAdapterProvider(params DxgiAdapterInfo[] adapters)
        => _adapters = adapters;

    public IReadOnlyList<DxgiAdapterInfo> GetAdapters()
        => _adapters;

    public static IDxgiAdapterProvider Throwing(Exception ex) => new ThrowingProvider(ex);

    private sealed class ThrowingProvider(Exception ex) : IDxgiAdapterProvider
    {
        public IReadOnlyList<DxgiAdapterInfo> GetAdapters()
            => throw ex;
    }
}
