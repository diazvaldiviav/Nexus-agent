---
name: Sprint 4 Day 7 — WindowsHostProfiler / WindowsHostProfilerTests Review
description: AC-3/AC-4 review findings for WindowsHostProfiler compositor and its NSubstitute tests
type: project
---

## Decision: CHANGES REQUIRED (1 HIGH, 3 MEDIUM, 4 LOW)

**Why:** One HIGH test assertion is a verified broken assertion — CpuState.Strong asserted for CpuInferenceScore=0.75, but classifier threshold is strict `< 0.75 => Strong`, so 0.75 maps to HighEnd.

**How to apply:** Do not approve until the CpuState threshold boundary is fixed.

## HIGH-1 (must fix)
WindowsHostProfilerTests.cs:48 — `AllSucceed_CompleteProfile` asserts `CpuState.Strong` for `ValidCpu` with `CpuInferenceScore = 0.75`. Classifier: `< CpuStrongThreshold (0.75) => Strong; _ => HighEnd`. Exactly 0.75 hits HighEnd. Fix: change score to 0.70 in ValidCpu, or change assertion to CpuState.HighEnd.

## MEDIUM-1
ValidCpu/ValidRam/ValidGpu declared as static properties (`=>`), not static readonly fields. Each test access allocates a new record instance. Convert to `private static readonly`.

## MEDIUM-2
ProfiledAt recency assertion duplicated in AllSucceed (line 51) and ProfiledAtIsRecent (line 163). Remove from AllSucceed — ProfiledAtIsRecent owns it.

## MEDIUM-3
`.Result` accessed on tasks after `Task.WhenAll` (lines 46-48). Functionally safe (tasks are complete), but violates the coding-standards SKILL's "NEVER .Result" rule. Fix: `var (cpu, ram, gpu) = (await cpuTask, await ramTask, await gpuTask);` — synchronous awaits on completed tasks, no cost.

## LOW-1
`ClassifyArchitecture_Theory` test name doesn't follow Method_Scenario_Expected convention.

## LOW-2
`WindowsHostProfiler` class missing `[SupportedOSPlatform("windows")]` attribute despite living in Nexus.Hardware.Windows and using Windows-only delegates.

## LOW-3
Inline fallback values in test assertions (`new CpuEnvelope("Unknown", 0, 0, 0, 1)`) are not linked to production static readonly fallback fields. Add comments.

## LOW-4
ProfileSafe<T> and BuildProfileAsync use ConfigureAwait(false) correctly — confirmed consistent.

## Positive Patterns
- Null guards on all three profilers, logger optional — AC-3 exact match
- CpuFallback/RamFallback/GpuFallback values match AC-3 exactly
- ClassifyArchitecture is internal static — testable without InternalsVisibleTo needed (same assembly group)
- Task.WhenAll + ProfileSafe<T> pattern isolates failures cleanly
- 7 tests present matching AC-4 exactly
- NSubstitute usage is idiomatic (.Returns, .ThrowsAsync)
- sealed class is correct
- SRP clean: classification → HostStateClassifier, profiling → injected profilers

## Recurring pattern to watch
Static property vs static readonly field for test fixtures — third occurrence (previously in OpenAI embedding tests). Project should standardise on `static readonly` for test data that never changes.
