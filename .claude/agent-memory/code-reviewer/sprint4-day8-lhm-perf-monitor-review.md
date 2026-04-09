---
name: Sprint 4 Day 8 — LHM + PerfCounter Monitor Review (US-2.5 / US-2.6)
description: Review findings for LhmSensorMonitor, LhmComputerWrapper, PerfCounterProvider, PerfCounterMonitor and their tests
type: project
---

## Decision: APPROVED WITH SUGGESTIONS (0 HIGH, 4 MEDIUM, 4 LOW)

All AC requirements met. No correctness bugs in production code. Issues are test quality, missing platform guards, and a subtle struct-default sentinel.

## MEDIUM-1 (SupportedOSPlatform missing)
LhmComputerWrapper, LhmSensorMonitor, PerfCounterProvider, PerfCounterMonitor all use Windows-only libraries (LibreHardwareMonitorLib, System.Diagnostics.PerformanceCounter) but have no [SupportedOSPlatform("windows")] attribute. WmiCpuProfiler and DxgiAdapterProvider have it — inconsistency. hardware-engineering SKILL requires this guard.

## MEDIUM-2 (Vacuous Dispose test in LhmSensorMonitorTests)
LhmSensorMonitorTests.Dispose_DisposesComputer (line 189) uses Assert.True(true, "...") — passes unconditionally regardless of what Dispose() does. FakeLhmComputer does not implement IDisposable, so the is-check always returns false and disposal is never verified. Fix: add a DisposableFakeLhmComputer wrapper (similar to DeleteFailingKnowledgeGraph pattern from MemoryCompressor review) that implements IDisposable and exposes Disposed bool.

## MEDIUM-3 (SelectPreferred null-struct sentinel is fragile)
LhmSensorMonitor.SelectPreferred (line 76) uses `preferred.SensorName is not null` to distinguish "no match found" from "match found". SensorName is declared as non-nullable `string` in LhmSensorReading, but `default(LhmSensorReading)` sets it to null at runtime. This works today but relies on undefined struct default behavior. Cleaner: change to `readings.Any(s => s.SensorName.Contains(...))` before FirstOrDefault, or change FirstOrDefault to return `LhmSensorReading?` via the predicate overload.

## MEDIUM-4 (FakeLhmComputer missing IDisposable — Dispose path untested)
FakeLhmComputer has a `Disposed` public property but does NOT implement IDisposable. LhmSensorMonitor.Dispose() conditionally disposes `if (_computer is IDisposable disposable)`. Since FakeLhmComputer never implements IDisposable, the disposal path is never exercised in tests. Exposed by the vacuous Dispose test. Add IDisposable to FakeLhmComputer with Disposed tracking (mirrors FakePerfCounterProvider pattern exactly).

## LOW-1 (SafeRead bare catch swallows all exceptions)
PerfCounterProvider.SafeRead (line 52) uses `catch { return 0f; }` — swallows ALL exceptions including OutOfMemoryException. Coding-standards SKILL flags bare catch as BAD. Change to `catch (Exception) { return 0f; }` minimum.

## LOW-2 (Missing CancellationToken in ReadSensors)
ILhmComputer.ReadSensors() has no CancellationToken parameter. LHM hardware.Update() can theoretically block if hardware access is slow. This is acceptable for the current synchronous design (offloaded to Task.Run), but worth noting for future API evolution.

## LOW-3 (Test count annotation — minor)
AC-6 says ~12 tests for LhmSensorMonitor. Actual count is 12. PASS.

## LOW-4 (FakeLhmComputer.Unavailable() static factory)
FakeLhmComputer has `Unavailable()` static factory (line 22) but no `Available(params LhmSensorReading[])` alias — minor asymmetry with Throwing() pattern. Not a functional issue.

## Good Patterns
- LhmComputerWrapper correctly calls hardware.Update() AND sub.Update() before collecting sensors — matches hardware-engineering SKILL §8.1
- IsMemoryEnabled=false — AC-3 exact match; reduces overhead
- LhmSensorMonitor.ReadCore() catch is broad (Exception) with log+null-return — correct for optional telemetry service
- PerfCounterMonitor uses three separate try/catch blocks (one per counter) — correct: a failure reading CPU load should not prevent RAM reading
- FakePerfCounterProvider.Dispose() sets Disposed=true AND Dispose_DisposesProvider correctly asserts it — clean pattern
- ConfigureAwait(false) on Task.Run call in LhmSensorMonitor.ReadAsync — consistent with project convention
- null-guard on _computer in LhmSensorMonitor constructor (ArgumentNullException) — correct
- null-guard on _provider in PerfCounterMonitor constructor (ArgumentNullException) — correct
- LhmSensorReading uses `readonly record struct` (stricter than AC requirement of `record struct`) — improvement
- CollectSensors correctly filters nulls (sensor.Value is null → continue) — AC-3 requirement met
- Both fakes follow FakeWmiQuery / FakeDxgiAdapterProvider patterns exactly (Throwing() with inner private sealed class)

## Patterns to watch in future
- [SupportedOSPlatform("windows")] is now missing from 4 new files in a row — standardise by adding to project-level suppressions or template
- Vacuous Assert.True(true, "...") as a placeholder test — flag immediately, it gives false confidence
