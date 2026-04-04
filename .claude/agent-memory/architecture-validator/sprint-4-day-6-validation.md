---
name: Sprint 4 Day 6 (WI-HRD-5) Validation
description: Validation result for WI-HRD-5 hardening — chat UX polish, CLI ConfigValidator integration, Nexus.Core + Nexus.Memory file reorganization, new tests
type: project
---

# Architecture Validation: WI-HRD-5 — Sprint 4 Day 6 Hardening

## Decision: APPROVED (0 HIGH, 2 MEDIUM, 4 LOW)

## Validation Summary

| Category | Status | Issues |
|---|---|---|
| C# / .NET Best Practices | PASS | 1 LOW |
| Dependency Injection | PASS | 0 |
| SOLID Principles | PASS | 0 |
| Layer Architecture | PASS | 1 MEDIUM (namespace rename risk) |
| Memory Layer Design | PASS | 0 |
| Error Handling | PASS | 1 MEDIUM |
| Testability | PASS | 1 LOW |
| Configuration | PASS | 0 |
| Desktop Integration | PASS | 1 LOW |

## Issues Found

### HIGH (must fix before implementation)

None.

### MEDIUM (should fix)

**MEDIUM-1: [Layer Architecture] Namespace rename breaks 38 `using Nexus.Core;` and 42 `using Nexus.Memory;` statements across 48 test + src files — no migration plan specified.**

The architecture doc says "All files get new namespace matching folder" (e.g., `Nexus.Core.Abstractions`, `Nexus.Core.Providers`, `Nexus.Memory.Graph`, etc.), but provides no guidance on:

1. Whether the old flat namespace (`Nexus.Core`, `Nexus.Memory`) is retained via `global using` aliases or if every consumer file must add new `using` statements.
2. Which consumers need updating: confirmed 25 test files with `using Nexus.Core` and 23 with `using Nexus.Memory`, plus all cross-project source files (Desktop, CLI, Connectors).

If step AC-13 ("All using statements updated across solution") is the entire migration plan, that is sufficient — but the doc must explicitly state that the OLD flat namespaces are GONE, and that AC-13 requires updating all 48 affected files. Without this clarity the implementer may incorrectly assume the flat namespaces are preserved.

Fix: Add to architecture doc — "Old flat namespaces (`Nexus.Core`, `Nexus.Memory`) are fully replaced. All consumers must update their `using` statements. No backward-compat alias is provided." Confirm AC-13 covers all 48 affected files.

**MEDIUM-2: [Error Handling] `ScrollViewer` `x:Name="MessagesScroller"` must exist in `ChatView.axaml` — but current AXAML uses a bare `<ScrollViewer>` with no Name attribute.**

The `ChatView.axaml.cs` design relies on `this.FindControl<ScrollViewer>("MessagesScroller")` in `OnLoaded`. The current `ChatView.axaml` (confirmed by reading source) has `<ScrollViewer IsVisible="{Binding HasMessages}">` with no `x:Name`. `FindControl` will return `null` for an unnamed control, `_scroller` will be null, and auto-scroll will silently never work. This is a silent failure — no error, no scroll.

Fix: Implementation step for ChatView.axaml must add `x:Name="MessagesScroller"` to the existing ScrollViewer element. This is a one-line AXAML change. Recommend explicitly calling this out as a required AXAML change in the implementation steps.

### LOW (nice to have)

**LOW-1: [C# / .NET Best Practices] `OnLoaded`/`OnUnloaded` data context event subscriptions assume DataContext is set at Loaded time — no guard for DataContext changes after Loaded.**

The design subscribes to `vm.Messages.CollectionChanged` in `OnLoaded` by casting `DataContext as ChatViewModel`. If DataContext is re-assigned after the view is loaded (e.g., during navigation), the new ViewModel's collection will not be subscribed and the old one will not be unsubscribed. In practice this is unlikely given `ChatView` has a fixed ViewModel, but the architecture doc is silent on this edge case.

Fix (implementer note): Add DataContextChanged subscription as a safety net, OR document the assumption that DataContext is set once before Loaded and never changed.

**LOW-2: [Testability] MainWindowViewModel navigation tests using "real ServiceCollection with fakes registered" requires all 4 ViewModels to have their full dependency chains stubbed.**

The architecture says 4 navigation tests via real ServiceCollection. Currently `ChatViewModel` requires `IAgentService`, `MemoryGraphViewModel` requires `IKnowledgeGraph`, `ActionLogViewModel` requires `IKnowledgeGraph` + `IActionLogNotifier` (same instance), `SettingsViewModel` requires `NexusConfig`. The test setup construction chain must register all 4 fakes correctly — especially the dual-registration of `FakeKnowledgeGraph` as both `IKnowledgeGraph` and `IActionLogNotifier`. Omitting the dual-registration causes `ActionLogViewModel` constructor to fail.

Fix (implementer note): In test ServiceCollection setup, ensure: `services.AddSingleton<IKnowledgeGraph>(fakeKg); services.AddSingleton<IActionLogNotifier>(fakeKg);` — same instance for both, matching the production DI pattern.

**LOW-3: [C# / .NET Best Practices] `OnUnloaded` does not call `base.OnUnloaded(e)` before returning — architecture code snippet shows `base.OnUnloaded(e)` AFTER the cleanup logic.**

This is actually correct ordering (cleanup first, then base), but worth confirming: the code snippet shows `base.OnUnloaded(e)` as the last statement in `OnUnloaded`. This mirrors `OnLoaded` where `base.OnLoaded(e)` is FIRST. The asymmetry (base first in Loaded, base last in Unloaded) is intentional and standard Avalonia lifecycle practice. No change needed — just confirming the implementer should not reorder.

**LOW-4: [Desktop Integration] `ScrollToBottom()` posts to `Dispatcher.UIThread` with `DispatcherPriority.Background` — this is correct for non-blocking scroll, but if multiple tokens arrive rapidly during streaming, multiple Background-priority posts will queue up.**

Each token triggers `OnLastMessagePropertyChanged` → `ScrollToBottom()` → one Dispatcher.Post. For high-frequency streaming (>10 tokens/sec) this could queue many no-op scroll posts (all resolved to the same final position). This is functionally correct but marginally inefficient.

Fix (optional): Add a `_scrollPending` bool flag: set true before posting, clear in the post body. Skip posting if `_scrollPending` is already true. This coalesces rapid-fire scroll requests into at most one pending post. Not required for correctness.

## Decision Rationale

APPROVED because:
- 0 HIGH issues: no blocking defects that prevent correct implementation
- Chat auto-scroll design is architecturally sound: view-only, correct event lifecycle (OnLoaded/OnUnloaded), correct 50px threshold, correct PropertyChanged tracking for streaming
- `_autoScrollEnabled` flag correctly suppresses scroll when user has scrolled up
- `UntrackLastMessage()` pattern correctly prevents double-subscribe/leak on rapid message additions
- CLI ConfigValidator integration is minimal and correct: `Phase 3.5` placement (after config load, before DI) is the right insertion point; `Markup.Escape()` prevents Spectre.Console markup injection
- ConfigValidator.Validate() already exists and works — confirmed by reading source
- Namespace reorganization (AC-11 through AC-14) is highest-risk but architecturally clean — no circular deps introduced, no layer violations. Risk is purely mechanical (rename scope). MEDIUM-1 documents the scope risk but it is manageable.
- All new test categories (MarkdownTextBlock lifecycle, ChatViewModel commands, MainWindowViewModel navigation, ConfigValidator bulk) are testing real behaviors with correct assertions

## Implementation Order Validation

The proposed order (tests → CLI → Chat UX → reorganization) is correct. File reorganization last is the right call — it is the highest blast-radius change and should be done after all other features are verified green.

## Codebase Facts Confirmed

- `ChatView.axaml` uses bare `<ScrollViewer>` with no `x:Name` — AXAML update required (MEDIUM-2)
- `ChatView.axaml.cs` currently has empty constructor only — full auto-scroll implementation is net-new
- `ConfigValidator.Validate()` exists at `src/Nexus.Core/Config/ConfigValidator.cs` and is complete — no new methods needed for CLI integration
- `Program.cs` Phase 3 currently ends at line 44 (`config = ConfigLoader.Load(configPath)`) — Phase 3.5 insertion point is between line 44 and line 47 (DI setup)
- All Nexus.Core files currently use flat namespace `Nexus.Core` — 18 files need namespace changes
- All Nexus.Memory files currently use flat namespace `Nexus.Memory` — ~16 files need namespace changes
- 25 test files reference `using Nexus.Core`, 23 reference `using Nexus.Memory` — all need using updates
- `ChatViewModel` already exists with `TestableChatViewModel` inner class — AC-8 tests (3 new ChatViewModel command tests) build on existing test infrastructure
- `FakeKnowledgeGraph` already exists in `tests/Nexus.Desktop.Tests/Fakes/` — AC-9 can reuse it
