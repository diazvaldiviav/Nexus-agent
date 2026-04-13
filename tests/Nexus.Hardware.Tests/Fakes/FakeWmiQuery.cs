using Nexus.Hardware.Windows.Internals;

namespace Nexus.Hardware.Tests.Fakes;

internal sealed class FakeWmiQuery : IWmiQuery
{
    private readonly IReadOnlyList<IReadOnlyDictionary<string, object>> _rows;

    public FakeWmiQuery(params Dictionary<string, object>[] rows)
        => _rows = rows;

    public IReadOnlyList<IReadOnlyDictionary<string, object>> Query(string wql)
        => _rows;

    public static IWmiQuery Throwing(Exception ex) => new ThrowingWmiQuery(ex);

    private sealed class ThrowingWmiQuery(Exception ex) : IWmiQuery
    {
        public IReadOnlyList<IReadOnlyDictionary<string, object>> Query(string wql)
            => throw ex;
    }
}
