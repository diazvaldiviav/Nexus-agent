---
name: Sprint 10 Validation
description: Validation result for Sprint 10 Critical Bug Fixes (Safety + Honesty) — AC-1 through AC-9
type: project
---

Sprint 10 (Critical Bug Fixes — Safety + Honesty): APPROVED WITH CONDITIONS (0 HIGH, 2 MEDIUM, 4 LOW)
File: docs/validation/sprint-10-validation.md

**Why:** Design is sound, thorough, and covers a large feature surface (permission gate, planner heuristic, path validator stale-state guard, summary grounding). The single AC deviation (IPermissionGate return type) was correctly reconciled by the architect and approved.

**How to apply:** Developer agent must be briefed on MEDIUM-1 and MEDIUM-2 before implementation.

**MEDIUM-1:** `AgentServicePermissionGateTests.cs` — architecture section 10.1 table lists 8 tests but section 11.7 adds `GateThrows_DefaultsToAllow_AndLogsWarning` as a 9th. The test table must be updated to include the 9th test. This test covers the safety behavior where gate exception defaults to Allow — important for production.

**MEDIUM-2:** `CliPermissionGate.RequestAsync` — the architecture documents `PromptUser` returning `(PermissionDecision, string?)` tuple but does not show the lift to `PermissionGateResponse` at the `RequestAsync` boundary. Implementer must assemble: `return new PermissionGateResponse(decision, feedbackText);`. If wrong, DenyWithFeedback silently returns null feedback → falls back to "user denied" losing the user's message.

**LOW-1:** `ConfigureAwait(false)` reminder for all await sites in `PersistentPermissionStore` (library code in Nexus.Core.Services).

**LOW-2:** No unit tests for `AutoApprovePermissionGate` inline tier-detection regex — add `AutoApprove_FullTierModel_ReturnsAllow` and `AutoApprove_SmallModelTier_ReturnsDeny` (6 lines each).

**LOW-3:** No YAML round-trip test for `permission:` section in `ConfigValidatorTests.cs` — add one test parsing `enabled: false` to confirm field is actually read, not defaulted.

**LOW-4:** Section 11.3 `CliPermissionGate` catch block documents returning `(Deny, "non-interactive prompt unavailable")` tuple — should be `new PermissionGateResponse(PermissionDecision.Deny, "non-interactive prompt unavailable")`.

**Codebase confirmed:**
- `SyntheticMarkers.cs` currently has 8 Prefixes (no PermissionDeniedMarker yet) — AC-5 adds the 9th.
- `AgentService` constructor currently has 17 params (no IPermissionGate or IVerificationCatalog yet).
- `IVerificationCatalog.VerificationRule` does NOT yet have `Destructive` field — AC-2 adds it.
- `NexusConfig.McpConfig` does NOT yet have `PlannerHeuristicEnabled`, `PlannerHeuristicMinLength`, `PathValidatorStrictDistance` — AC-1/AC-7 add them.

**Approved deviation:** `IPermissionGate.RequestAsync` returns `Task<PermissionGateResponse>` (record) instead of `Task<PermissionDecision>` (bare enum). Required because enums cannot carry payload; AC-5 pseudocode uses `dwf.Feedback`. All 5 enum values preserved verbatim.

**New pattern from this sprint:** When an interface's return type is an enum and the caller needs to extract payload from one case, always wrap in a record `(Decision, Payload?)` rather than using thread-locals or out-parameters. This is the clean C# solution for discriminated-union-like behavior.
