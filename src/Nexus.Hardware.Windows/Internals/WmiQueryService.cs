using System.Management;
using System.Runtime.Versioning;

namespace Nexus.Hardware.Windows.Internals;

[SupportedOSPlatform("windows")]
internal sealed class WmiQueryService : IWmiQuery
{
    public IReadOnlyList<IReadOnlyDictionary<string, object>> Query(string wql)
    {
        var results = new List<Dictionary<string, object>>();
        using var searcher = new ManagementObjectSearcher(wql);
        using var collection = searcher.Get();
        foreach (ManagementObject obj in collection)
        {
            try
            {
                var dict = new Dictionary<string, object>();
                foreach (var prop in obj.Properties)
                    dict[prop.Name] = prop.Value;
                results.Add(dict);
            }
            finally
            {
                obj.Dispose();
            }
        }
        return results;
    }
}
