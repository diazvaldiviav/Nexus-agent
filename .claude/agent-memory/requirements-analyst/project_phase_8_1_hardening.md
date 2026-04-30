---
name: Phase 8.1 Plan-then-Execute Hardening
description: Requirements analysis artifact for US-sprint-phase-8-1 — bringing plan-then-execute to Phase-7 hardened baseline. 15 ACs across 6 categories. Requirements doc at docs/requirements/US-sprint-phase-8-1-hardening-requirements.md.
type: project
---

# Phase 8.1 — Plan-then-Execute Hardening

Purpose: bring Phase 8 (`ToolPlanner` + `ExecutePlanAsync` / `ExecutePlanStreamAsync`) to the same hardened baseline Phase 7 tool-filtering reached at commit **ec8307a**. Opt-in activation of `mcp.tool_planning_enabled: true` requires safe production readiness on `qwen3:1.7b`.

**Why:** Two independent audits surfaced 3 HIGH-severity correctness gaps + 8 robustness/parity/UX gaps. Enabling this flag in production today has known hang / partial-abort / extraction-leakage risks.

**How to apply:**
- When future work touches `ToolPlanner`, `AgentService.ExecutePlanAsync`, or `ExecutePlanStreamAsync`, check that these 15 ACs are already met (or being addressed).
- Phase 7 commit **ec8307a** is the canonical template for "opt-in feature → production-ready" hardening in this codebase.
- When adding new opt-in features, apply the same template: null safety, logging parity, per-feature config timeout + validator, Desktop UI toggle + snapshot extension, XML docs, named constants, ~12 tests split across unit/integration/validator/VM layers.

## Blocker summary (ACs A1-A3)
- **A1:** `ExecuteToolWithTimeoutAsync` in plan paths currently aborts entire plan on any per-step exception — must become per-step try/catch with OCE filter.
- **A2:** Synthetic `"Execute ONLY this step"` directives leak into background entity extraction as fake user intent — must be tagged with `[PLANNER] ` sentinel prefix and filtered in `RunBackgroundExtraction`.
- **A3:** `ToolPlanner.GeneratePlanAsync` has no independent LLM timeout — a hung local LLM hangs the whole agent. Linked CTS combining caller `ct` + new `Mcp.ToolPlanningTimeoutSeconds` (int, default 30, range 5..300).

## Phase 7 reuse template (ec8307a)
- `ValidateToolFilteringEnabled` → clone as `ValidateToolPlanningEnabled`.
- `ToolFilteringEnabled` ObservableProperty + snapshot field → clone as `ToolPlanningEnabled`, extending `SettingsSnapshot` from **18 → 19 fields**.
- ToggleSwitch in `SettingsView.axaml` MCP Tool Settings section → add adjacent row.
- Tests split: validator tests in `Nexus.Core.Tests.ConfigValidatorTests`, VM dirty-tracking tests in `Nexus.Desktop.Tests.SettingsViewModelValidationTests`.

## Key integration points (AgentService.cs line refs)
- `ExecutePlanAsync` ~L367 (tool-wrap + log truncation + sentinel + plan-trail constant)
- `ExecutePlanStreamAsync` ~L493 (same + summary stream try/catch emitting `[Summary unavailable:`)
- `RunBackgroundExtraction` ~L627 (filter `StartsWith("[PLANNER] ")`)
- Named plan-trail constants replace `plan.Steps.Count * 3 + 4` at ~L481, ~L619

## Scope notes
- `EntityExtractor.ExtractAndPersistAsync` may need to become `virtual` to enable F2-2 test capturing (alternative: extract `IEntityExtractor` — larger scope). Prefer virtual.
- Cosine becomes an instance method on ToolPlanner so `_loggedDimMismatch` field can be used (one-shot warning per planner instance — Singleton DI).
- Embedding cache bounded at 1024 entries (`EmbeddingCacheMaxEntries` const) — one-shot warning on cap hit.

## Acceptance criteria → file map cheatsheet
| Category | Files |
|---|---|
| A (correctness) | `AgentService.cs` + `ToolPlanner.cs` + `NexusConfig.cs` |
| B (parity) | `AgentService.cs` |
| C (robustness) | `ToolPlanner.cs` |
| D (config + UI) | `ConfigValidator.cs` + `SettingsViewModel.cs` + `SettingsView.axaml` |
| E (docs + consts) | `IToolPlanner.cs` + `ToolPlan.cs` + `ToolPlanner.cs` + `AgentService.cs` |
| F (tests) | `ToolPlannerTests.cs` + `AgentServicePlanExecutionTests.cs` + `ConfigValidatorTests.cs` + `SettingsViewModelValidationTests.cs` (13 new tests) |

## Top risk to watch
R-5: `_loggedDimMismatch` / `_loggedCacheCap` are non-volatile fields on a Singleton. Benign race (2 warnings instead of 1) — acceptable; use `Interlocked.CompareExchange` only if strict once-only is required.
