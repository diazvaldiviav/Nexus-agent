---
name: CLI MCP Command Patterns
description: US-3.3 review findings for connect/disconnect/servers/help — spinner output placement, disconnect catch pattern, servers table args gap, help alignment
type: project
---

US-3.3 CLI review (2026-03-30): 3 MEDIUM findings in connect/disconnect/servers commands.

**Key findings:**

1. `RunConnectCommandAsync` — `AnsiConsole.Status().StartAsync(...)` wraps success/failure MarkupLine calls *inside* the lambda. Output should be emitted after the status context closes to avoid interleaving with spinner on some terminals.

2. `RunDisconnectCommandAsync` — Green success line is emitted inside a `catch` block (config save failure path), giving the impression of full success while an error occurred. Success line should be unconditional before the try/catch since disconnect itself always succeeds at that point.

3. `RunServersCommand` — `Command/URL` column for stdio servers shows only `server.Command` with no args. Correct pattern (already used in `ConnectMcpServersAsync` line 122) is `$"{server.Command} {string.Join(" ", server.Args)}".Trim()`.

**Patterns confirmed compliant:**
- All user-provided strings correctly escaped via `EscapeMarkup()` / `Markup.Escape()`
- Empty state messages are actionable and consistent with rest of file
- Error messages provide actionable hints

**Why:** Spinner interleaving and green-in-catch are CLI reliability signals that affect user trust in the tool, not just cosmetic issues.

**How to apply:** When reviewing future CLI commands that use `AnsiConsole.Status().StartAsync(...)`, verify that all output MarkupLine calls are outside the lambda. When a success message is emitted near an exception handler for a non-fatal error, verify the success line is outside the catch.
