namespace Nexus.Hardware.Windows.Internals;

internal interface IWmiQuery
{
    IReadOnlyList<IReadOnlyDictionary<string, object>> Query(string wql);
}
