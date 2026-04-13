# Skill: Hardware Engineering — Nexus Hardware Intelligence (.NET 10)

> Advanced hardware detection, profiling, and model-hardware matching for the Nexus Hardware Intelligence system. Load this skill when designing or implementing any hardware profiling, GPU detection, memory estimation, or model recommendation logic.

---

## 1. Windows Hardware Detection Stack

### 1.1 Technology Matrix

| Technology | Purpose | Speed | Permissions | NuGet Package |
|---|---|---|---|---|
| **System.Management (WMI)** | CPU identity, architecture, core count | 50-200ms/query | User | `System.Management` |
| **GlobalMemoryStatusEx** | RAM total/available, commit, pressure | <1ms | User | Built-in (P/Invoke) |
| **DXGI (Vortice.Windows)** | GPU enumeration, VRAM budget, vendor | <2ms | User | `Vortice.DXGI` |
| **System.Runtime.Intrinsics** | SIMD capability detection | 0ms (JIT const) | User | Built-in |
| **RuntimeInformation** | Architecture, OS, emulation detection | 0ms | User | Built-in |
| **LibreHardwareMonitorLib** | Temperatures, clocks, load, power | 10-50ms | **Admin** | `LibreHardwareMonitorLib` |
| **PerformanceCounter** | CPU %, available RAM, page faults | 1-5ms (after init) | User | `System.Diagnostics.PerformanceCounter` |

### 1.2 Recommended Priority

1. **GlobalMemoryStatusEx** — Always use for RAM (fastest, most accurate)
2. **DXGI via Vortice.Windows** — Always use for GPU (fast, real-time VRAM budget)
3. **System.Runtime.Intrinsics** — Always use for SIMD detection (zero cost)
4. **RuntimeInformation** — Always use for architecture (zero cost)
5. **WMI** — Use for CPU identity only (slow but necessary for name/model)
6. **LibreHardwareMonitorLib** — Optional enrichment only (requires admin)
7. **PerformanceCounter** — Optional continuous monitoring (legacy but functional)

---

## 2. WMI (System.Management) — Rules & Pitfalls

### 2.1 Correct Usage Pattern

```csharp
[SupportedOSPlatform("windows")]
public class WmiCpuProfiler : ICpuProfiler
{
    public async Task<CpuEnvelope> ProfileAsync()
    {
        // WMI is COM-based synchronous — MUST offload to thread pool
        return await Task.Run(() =>
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, Architecture "
                + "FROM Win32_Processor");
            using var results = searcher.Get();

            foreach (ManagementObject obj in results)
            {
                try
                {
                    var name = obj["Name"]?.ToString() ?? "Unknown";
                    var cores = Convert.ToInt32(obj["NumberOfCores"]);
                    // ... build envelope
                }
                finally
                {
                    obj.Dispose(); // CRITICAL: each ManagementObject holds COM ref
                }
            }
            // ...
        });
    }
}
```

### 2.2 Critical Rules

| Rule | Why |
|---|---|
| **Always `Task.Run` for WMI calls** | WMI is synchronous COM. Blocks calling thread for 50-200ms. Never call on UI thread. |
| **Dispose ManagementObjectSearcher** | Holds COM RCW references. Leaks handles if not disposed. |
| **Dispose ManagementObjectCollection** | The `Get()` result also holds COM state. Use `using`. |
| **Dispose each ManagementObject** | Enumerated objects hold individual COM refs. Dispose in finally block. |
| **Never use `GetAsync`** | Legacy WMI async is unreliable in modern .NET. Use `Task.Run` + sync `Get()`. |
| **Create searcher per-call** | `ManagementObjectSearcher` is NOT thread-safe. No reuse. |
| **Guard with `OperatingSystem.IsWindows()`** | Throws `PlatformNotSupportedException` on non-Windows. |
| **Catch `ManagementException`** | WMI service may be disabled, query may be invalid. |
| **Catch `COMException`** | RPC failures, provider crashes, timeout. |

### 2.3 Key WMI Classes

| Class | Key Properties | Notes |
|---|---|---|
| `Win32_Processor` | Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, Architecture | Architecture: 0=x86, 5=ARM, 9=x64, 12=ARM64 |
| `Win32_OperatingSystem` | TotalVisibleMemorySize, FreePhysicalMemory | Values in **KB** (not bytes) |
| `Win32_ComputerSystem` | TotalPhysicalMemory, Model, Manufacturer | TotalPhysicalMemory in bytes |
| `Win32_VideoController` | Name, AdapterRAM | **AVOID** — slow (200-500ms), inaccurate VRAM. Use DXGI instead. |

### 2.4 Common Bugs

1. **`Win32_VideoController.AdapterRAM` returns wrong value** — Capped at 4GB (uint32 overflow). Always use DXGI for VRAM.
2. **First WMI query in process is 500ms+** — WMI service cold start. Cache results aggressively.
3. **WMI returns empty on minimal Windows installs** — Server Core or stripped images may lack WMI providers.
4. **`NumberOfCores` vs `NumberOfLogicalProcessors`** — Cores are physical, logical includes hyperthreading. For thread count use logical.

---

## 3. GlobalMemoryStatusEx — P/Invoke Reference

### 3.1 Correct Implementation

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct MEMORYSTATUSEX
{
    public uint dwLength;
    public uint dwMemoryLoad;        // 0-100 percentage of physical RAM in use
    public ulong ullTotalPhys;       // Total physical RAM (bytes)
    public ulong ullAvailPhys;       // Available physical RAM (bytes)
    public ulong ullTotalPageFile;   // Total commit limit (bytes)
    public ulong ullAvailPageFile;   // Available commit (bytes)
    public ulong ullTotalVirtual;    // Total virtual address space (bytes)
    public ulong ullAvailVirtual;    // Available virtual address space (bytes)
    public ulong ullAvailExtendedVirtual; // Reserved, always 0
}

[LibraryImport("kernel32.dll", SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
private static partial bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

public static MEMORYSTATUSEX GetMemoryStatus()
{
    var status = new MEMORYSTATUSEX();
    status.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>(); // CRITICAL — must set before call
    if (!GlobalMemoryStatusEx(ref status))
        throw new Win32Exception(Marshal.GetLastPInvokeError());
    return status;
}
```

### 3.2 Critical Rules

| Rule | Why |
|---|---|
| **Set `dwLength` before calling** | API silently fails and returns zeroes without it |
| **Use `[LibraryImport]` over `[DllImport]`** | .NET 7+ source-generated, better AOT/trimming support |
| **Use `Marshal.GetLastPInvokeError()`** | Thread-safe alternative to `GetLastWin32Error()` |
| **Struct is fully blittable** | All `uint`/`ulong` — zero marshaling overhead |

### 3.3 RAM Budget Formulas

```csharp
// Available right now (volatile — changes constantly)
long usableRamNow = (long)status.ullAvailPhys;

// Safe budget for loading a model (70% of available — leave 30% for OS + apps)
long safeModelRamBudget = (long)(status.ullAvailPhys * 0.70);

// Safe budget during inference (85% of model budget — leave room for KV cache growth)
long safeInferenceRamBudget = (long)(safeModelRamBudget * 0.85);

// Commit headroom (swap/pagefile capacity)
double commitUsageRatio = 1.0 - ((double)status.ullAvailPageFile / status.ullTotalPageFile);

// Pressure levels
RamPressureLevel pressure = status.dwMemoryLoad switch
{
    >= 95 => RamPressureLevel.Critical,  // System thrashing
    >= 85 => RamPressureLevel.High,      // Significant swap activity
    >= 70 => RamPressureLevel.Medium,    // Noticeable pressure
    >= 50 => RamPressureLevel.Low,       // Comfortable
    _     => RamPressureLevel.None       // Plenty of headroom
};
```

---

## 4. DXGI GPU Detection — via Vortice.Windows

### 4.1 NuGet Package

```xml
<PackageReference Include="Vortice.DXGI" Version="3.7.*" />
```

Vortice.Windows is the modern successor to SharpDX. Provides idiomatic `IDisposable` wrappers over all DXGI COM interfaces. Targets .NET 8+.

### 4.2 Correct Usage Pattern

```csharp
using Vortice.DXGI;

public class DxgiGpuProfiler : IGpuProfiler
{
    public Task<GpuEnvelope> ProfileAsync()
    {
        // DXGI is fast (<2ms) — can run synchronously, but Task.Run for consistency
        return Task.Run(() =>
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

            IDXGIAdapter1? bestAdapter = null;
            long maxDedicatedVram = 0;

            for (int i = 0; factory.EnumAdapters1(i, out var adapter).Success; i++)
            {
                using (adapter)
                {
                    var desc = adapter.Description1;

                    // Skip software/WARP adapters
                    if ((desc.Flags & AdapterFlags.Software) != 0) continue;

                    if ((long)desc.DedicatedVideoMemory > maxDedicatedVram)
                    {
                        bestAdapter?.Dispose();
                        maxDedicatedVram = (long)desc.DedicatedVideoMemory;
                        bestAdapter = adapter; // Transfer ownership — do NOT dispose this one
                        adapter = null!;       // Prevent using-dispose
                    }
                }
            }

            if (bestAdapter is null)
                return GpuEnvelope.NoGpu(); // Integrated-only or no GPU

            using (bestAdapter)
            {
                var desc = bestAdapter.Description1;
                long dedicatedVram = (long)desc.DedicatedVideoMemory;
                long sharedMemory = (long)desc.SharedSystemMemory;

                // Query real-time VRAM budget (IDXGIAdapter3, Windows 10+)
                long localBudget = dedicatedVram; // fallback
                long localUsage = 0;

                if (bestAdapter.QueryInterface<IDXGIAdapter3>() is IDXGIAdapter3 adapter3)
                {
                    using (adapter3)
                    {
                        var memInfo = adapter3.QueryVideoMemoryInfo(0, MemorySegmentGroup.Local);
                        localBudget = (long)memInfo.Budget;
                        localUsage = (long)memInfo.CurrentUsage;
                    }
                }

                long availableVram = localBudget - localUsage;
                long safeGpuBudget = (long)(availableVram * 0.85); // 15% safety margin

                return new GpuEnvelope(/* ... */);
            }
        });
    }
}
```

### 4.3 Critical Rules

| Rule | Why |
|---|---|
| **Always dispose DXGI objects** | COM references leak if not released. Vortice wraps as IDisposable. |
| **Skip `AdapterFlags.Software`** | WARP software renderer is not useful for ML inference |
| **Use `IDXGIAdapter3` for VRAM budget** | `DedicatedVideoMemory` is total VRAM. Budget is what's actually available now. |
| **Budget includes OS reservation** | Windows desktop compositor uses 1-2 GB VRAM. Budget reflects this. |
| **QueryVideoMemoryInfo requires Windows 10+** | Fails on Windows 8.x. Fallback to `DedicatedVideoMemory`. |
| **Integrated GPUs: DedicatedVideoMemory = 0** | Use `SharedSystemMemory` but it competes with system RAM — NOT additive. |
| **Don't use Win32_VideoController** | Slow (200-500ms), AdapterRAM capped at 4GB (uint32), no real-time budget. |

### 4.4 Multi-GPU Selection

```
1. Enumerate all adapters
2. Skip FLAG_SOFTWARE (WARP)
3. Select adapter with highest DedicatedVideoMemory
4. If all DedicatedVideoMemory == 0 → integrated-only system
5. Report as GpuState.None if no discrete GPU found
```

---

## 5. SIMD & Architecture Detection

### 5.1 SIMD Capability (Zero Runtime Cost)

```csharp
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics.Arm;

public static class SimdDetector
{
    // These are JIT constants — evaluated at compile time after tiered compilation
    public static bool HasSse42  => Sse42.IsSupported;
    public static bool HasAvx    => Avx.IsSupported;
    public static bool HasAvx2   => Avx2.IsSupported;
    public static bool HasAvx512 => Avx512F.IsSupported;
    public static bool HasNeon   => AdvSimd.IsSupported;    // ARM NEON

    public static double ComputeSimdScore()
    {
        if (HasAvx512) return 1.0;
        if (HasAvx2)   return 0.75;
        if (HasAvx)    return 0.50;
        if (HasSse42)  return 0.30;
        if (HasNeon)   return 0.60;  // ARM NEON is roughly AVX-equivalent for inference
        return 0.10;                 // Scalar fallback
    }
}
```

### 5.2 Architecture & Emulation Detection

```csharp
using System.Runtime.InteropServices;

// True OS architecture (x64, ARM64, etc.)
Architecture osArch = RuntimeInformation.OSArchitecture;

// Process architecture (may differ if running under emulation)
Architecture processArch = RuntimeInformation.ProcessArchitecture;

ArchitectureState state = (osArch, processArch) switch
{
    (Architecture.X64, Architecture.X64)     => ArchitectureState.NativeOptimal,
    (Architecture.Arm64, Architecture.Arm64) => ArchitectureState.NativeOptimal,
    (Architecture.Arm64, Architecture.X64)   => ArchitectureState.EmulatedPenalty, // x64 on ARM64
    (Architecture.X64, Architecture.X86)     => ArchitectureState.NativeCompatible, // WoW64
    _                                        => ArchitectureState.Unsupported
};
```

**Key insight:** ARM64 Windows running x64 processes incurs ~20-30% performance penalty for compute-heavy workloads (like LLM inference). The recommendation engine must detect this and penalize accordingly.

---

## 6. LLM Model Memory Estimation

### 6.1 Quantization Reference Table

| Quantization | Bytes/Param | Quality Impact | Use Case |
|---|---|---|---|
| FP32 | 4.000 | Baseline (training) | Never for inference |
| FP16 / BF16 | 2.000 | Negligible loss | GPU inference with ample VRAM |
| Q8_0 | ~1.000 | Negligible loss | High-quality CPU/GPU inference |
| Q6_K | ~0.830 | Minimal loss | Quality-first inference |
| Q5_K_M | ~0.625 | Very slight loss | **Recommended balance** |
| Q5_K_S | ~0.625 | Slight loss | Slightly less quality than K_M |
| Q4_K_M | ~0.500 | Acceptable loss | **Most popular**, best size/quality tradeoff |
| Q4_K_S | ~0.500 | Moderate loss | Smaller than K_M, slightly worse |
| Q4_0 | ~0.500 | Moderate loss | Legacy, prefer K variants |
| Q3_K_M | ~0.4375 | Noticeable loss | RAM-constrained systems |
| Q2_K | ~0.3125 | Significant loss | Emergency / research only |

**K-quants** (K_S, K_M, K_L) use mixed precision: attention/output layers get higher bits, feed-forward gets lower. K_M keeps more layers at higher precision than K_S.

### 6.2 Memory Estimation Formulas

```csharp
public static class ModelMemoryEstimator
{
    // Weight size = parameters × bytes-per-parameter
    public static long EstimateWeightSize(long paramCountMillions, string quantization)
    {
        double bpp = GetBytesPerParam(quantization);
        return (long)(paramCountMillions * 1_000_000L * bpp);
    }

    // RAM to load model (weights + overhead)
    public static long EstimateRamOnLoad(long paramCountMillions, string quantization)
    {
        long weightSize = EstimateWeightSize(paramCountMillions, quantization);
        double overheadFactor = paramCountMillions < 1000 ? 1.20 : 1.15; // smaller models have higher relative overhead
        return (long)(weightSize * overheadFactor);
    }

    // KV Cache for context window
    public static long EstimateKvCache(int numLayers, int headDim, int numKvHeads,
                                        int contextLength, double bytesPerKvValue = 2.0)
    {
        // 2 (K+V) × layers × context × (headDim × kvHeads) × bytesPerValue
        return (long)(2.0 * numLayers * contextLength * headDim * numKvHeads * bytesPerKvValue);
    }

    // Total inference RAM
    public static long EstimateRamOnInference(long ramOnLoad, long kvCache)
    {
        long scratchBuffer = 512 * 1024 * 1024; // 512 MB typical
        return ramOnLoad + kvCache + scratchBuffer;
    }

    // VRAM for full GPU offload
    public static long EstimateVramFullOffload(long ramOnLoad, long kvCache)
    {
        return ramOnLoad + kvCache; // Everything in VRAM
    }

    // VRAM for partial offload (N layers)
    public static long EstimateVramPartialOffload(long weightSize, int totalLayers, int offloadLayers)
    {
        return (long)((double)offloadLayers / totalLayers * weightSize);
    }

    private static double GetBytesPerParam(string quantization) => quantization.ToUpperInvariant() switch
    {
        "FP32"   => 4.0,
        "FP16" or "BF16" => 2.0,
        "Q8_0"   => 1.0,
        "Q6_K"   => 0.83,
        "Q5_K_M" or "Q5_K_S" or "Q5_K_L" => 0.625,
        "Q4_K_M" or "Q4_K_S" or "Q4_K_L" or "Q4_0" => 0.5,
        "Q3_K_M" or "Q3_K_S" or "Q3_K_L" => 0.4375,
        "Q2_K"   => 0.3125,
        "IQ4_XS" or "IQ4_NL" => 0.5,
        "IQ3_XXS" => 0.39,
        "IQ2_XXS" => 0.28,
        _ => 0.5 // Default to Q4 estimate
    };
}
```

### 6.3 Common Estimation Pitfalls

| Pitfall | Impact | Mitigation |
|---|---|---|
| **Ollama reports file size, not runtime memory** | 10-20% underestimate | Apply overhead factor (1.15-1.20) |
| **Ignoring KV cache for long context** | 8K→128K = 16x KV cache increase | Always include context length in estimate |
| **Shared VRAM (iGPU) treated as additive** | Double-counting RAM | If DedicatedVideoMemory == 0, shared VRAM competes with system RAM |
| **Windows VRAM reservation ignored** | 1-2 GB unavailable for models | Use DXGI Budget (not total) as the baseline |
| **Memory fragmentation** | Model may not load despite sufficient free RAM | Require 10-15% margin above theoretical minimum |
| **GQA architecture reduces KV cache** | Overestimate for Llama 3, Mistral | Use actual `numKvHeads` (not `numAttentionHeads`) |

---

## 7. Safety Margins & Thresholds

### 7.1 RAM Safety

```
SafeModelRamBudget    = AvailablePhysicalRAM × 0.70  (leave 30% for OS + apps)
SafeInferenceRamBudget = SafeModelRamBudget × 0.85   (leave 15% for KV cache growth)
```

### 7.2 VRAM Safety

```
SafeGpuBudget = DXGIBudget - DXGICurrentUsage) × 0.85  (leave 15% for driver overhead)
```

### 7.3 Feasibility Thresholds

| Safety Level | RAM Margin | Meaning |
|---|---|---|
| **Unsafe** | < 0% | Model doesn't fit — REJECT |
| **Caution** | 0-15% | Tight fit, may swap under load |
| **Safe** | 15-40% | Comfortable with room for KV cache |
| **Comfortable** | > 40% | Plenty of headroom |

### 7.4 CPU Thread Budget

```csharp
int maxSafeCpuThreads = Math.Max(1, Environment.ProcessorCount - 2);
// Reserve 2 threads for OS + app responsiveness
```

---

## 8. LibreHardwareMonitorLib — Optional Telemetry

### 8.1 Usage Pattern

```csharp
// REQUIRES ADMIN PRIVILEGES — sensors return null/0 without elevation
var computer = new Computer
{
    IsCpuEnabled = true,
    IsGpuEnabled = true,
    IsMemoryEnabled = true
};

computer.Open(); // Must call before reading

foreach (var hardware in computer.Hardware)
{
    hardware.Update(); // Must call per-hardware per-read cycle

    foreach (var sensor in hardware.Sensors)
    {
        // sensor.SensorType: Temperature, Clock, Load, Power, Fan, Voltage
        // sensor.Value: nullable float
    }
}

computer.Close(); // IDisposable — must close to release kernel handles
```

### 8.2 Rules

- **NOT a replacement for WMI/DXGI** — complementary telemetry only
- **Fails gracefully without admin** — returns null values, does not throw
- **Disable via configuration** — not mandatory for recommendation engine
- **Use for thermal risk assessment** — high CPU/GPU temp → `ThermalRiskLevel.High`

---

## 9. P/Invoke Best Practices (.NET 7+)

| Practice | Details |
|---|---|
| **`[LibraryImport]` over `[DllImport]`** | Source-generated marshaling, AOT-compatible, explicit |
| **`SetLastError = true`** | Required for Win32 error reporting |
| **`Marshal.GetLastPInvokeError()`** | Thread-safe (preferred over `GetLastWin32Error()`) |
| **`SafeHandle` over `IntPtr`** | Deterministic cleanup, exception-safe |
| **Blittable types preferred** | `int`, `uint`, `ulong`, `byte*` — zero marshaling cost |
| **`[MarshalAs(UnmanagedType.Bool)]` for bool** | `bool` is non-blittable in C — explicit marshaling required |
| **`static partial class`** | Required for `[LibraryImport]` source generator |
| **Guard with `[SupportedOSPlatform("windows")]`** | Compile-time platform check |

---

## 10. Architecture Decision Records

### ADR-1: DXGI via Vortice.Windows, NOT Win32_VideoController

**Decision:** Use `Vortice.DXGI` NuGet for all GPU detection.
**Why:** Win32_VideoController is slow (200-500ms), `AdapterRAM` overflows at 4GB (uint32), and has no real-time budget query. DXGI completes in <2ms, supports IDXGIAdapter3 budget queries, and reports accurate VRAM on modern GPUs.

### ADR-2: GlobalMemoryStatusEx, NOT WMI for RAM

**Decision:** Use P/Invoke `GlobalMemoryStatusEx` for all RAM metrics.
**Why:** Executes in <1ms (vs 50-100ms for WMI `Win32_OperatingSystem`), returns bytes (not KB requiring conversion), includes commit/pagefile data, and requires no special permissions.

### ADR-3: Parallel Profiling Where Possible

**Decision:** CPU (WMI), RAM (P/Invoke), and GPU (DXGI) profilers run in parallel.
**Why:** WMI is the bottleneck at 50-200ms. Running all three concurrently via `Task.WhenAll` reduces total profiling time from ~250ms sequential to ~200ms parallel.

### ADR-4: Safety-First Memory Budgets

**Decision:** All memory budgets include safety margins (70% RAM, 85% VRAM).
**Why:** Memory fragmentation, OS overhead, and concurrent app usage make theoretical maximums unsafe. A model that barely fits will cause swapping, degrading the entire system.

### ADR-5: Immutable Profile Snapshots

**Decision:** All hardware profiles are immutable records, timestamped.
**Why:** Hardware state changes continuously (RAM availability, VRAM budget). Decisions must be based on a consistent snapshot, not live readings that shift mid-evaluation.
