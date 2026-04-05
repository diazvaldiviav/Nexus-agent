using Vortice.DXGI;

namespace Nexus.Hardware.Windows.Internals;

internal sealed class DxgiAdapterProvider : IDxgiAdapterProvider
{
    public IReadOnlyList<DxgiAdapterInfo> GetAdapters()
    {
        var adapters = new List<DxgiAdapterInfo>();

        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

        for (uint i = 0; factory.EnumAdapters1(i, out var adapter).Success; i++)
        {
            using (adapter)
            {
                var desc = adapter.Description1;

                if ((desc.Flags & AdapterFlags.Software) != 0)
                    continue;

                long localBudget = 0;
                long localUsage = 0;

                try
                {
                    using var adapter3 = adapter.QueryInterface<IDXGIAdapter3>();
                    var memInfo = adapter3.QueryVideoMemoryInfo(0, MemorySegmentGroup.Local);
                    localBudget = (long)memInfo.Budget;
                    localUsage = (long)memInfo.CurrentUsage;
                }
                catch (Exception)
                {
                    // QueryInterface or QueryVideoMemoryInfo failed — budget info unavailable.
                    // DxgiGpuProfiler handles 0-budget fallback to DedicatedVideoMemory (AC-19).
                }

                adapters.Add(new DxgiAdapterInfo(
                    desc.Description,
                    desc.VendorId,
                    (long)desc.DedicatedVideoMemory,
                    (long)desc.SharedSystemMemory,
                    localBudget,
                    localUsage,
                    IsHardware: true));
            }
        }

        return adapters;
    }
}
