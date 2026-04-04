---
name: Onboarding Wizard UX Patterns
description: US-3.4 review findings for CLI first-use wizard — API key masking, ollama pull progress, spinner output ordering, step numbering, auto-trigger scope
type: project
---

US-3.4 CLI onboarding wizard review (2026-03-30): 2 HIGH, 4 MEDIUM, 3 LOW findings.

**HIGH findings:**

1. `CollectApiKeys()` — All three `TextPrompt<string>` calls lack `.IsSecret()`. API keys are echoed in plaintext as the user types. Fix: add `.IsSecret()` to each `TextPrompt`.

2. `CheckModelAsync` — `ollama pull` subprocess runs inside `AnsiConsole.Status().StartAsync(...)` with `RedirectStandardOutput = true`. All download progress from ollama is swallowed. User sees a frozen spinner for several minutes on multi-GB model downloads. Fix: do not redirect stdout, do not wrap in spinner — let ollama render progress natively to the inherited console.

**MEDIUM findings:**

3. Success `MarkupLine` inside `Status().StartAsync()` lambda — same interleaving risk as US-3.3 finding #1. Emit result lines after the `await ... StartAsync(...)` returns. (Blocked by HIGH finding #2 — resolved automatically if pull is moved outside spinner.)

4. No step numbering — 6-step wizard presents as flat stream. Add numbered headers `[1/6] Step name...` for orientation.

5. Ollama-not-detected message has no bridge to cloud-provider path. User does not know setup continues with cloud providers. Add: "You can still use cloud providers — enter API keys in the next step."

6. Auto-trigger scope too broad — `nexus "question"` with no config fires the interactive wizard instead of failing fast. Tighten auto-trigger condition to `isInteractiveMode = filteredArgs.Length == 0 || filteredArgs[0] == "chat"`.

**LOW findings:**

7. No model selection offered when recommended model is absent but other models are already installed.

8. Ollama detection spinner (10s timeout) gives no timeout hint to user.

9. Summary always prints chat/embed model names even when Ollama was absent. Should add `[dim](requires Ollama)[/]` qualifier.

**Why:** HIGH #1 is a security UX failure (API key visible in terminal history/screen). HIGH #2 will cause users to believe the app has hung during a large pull — this is the primary first-run interaction and the most likely support request trigger.

**How to apply:** When reviewing any future CLI prompt that collects secrets (API keys, passwords, tokens), always check for `.IsSecret()`. When reviewing any future use of `AnsiConsole.Status().StartAsync(...)` wrapping a subprocess, verify the subprocess output path — if it emits progress lines, do not redirect and do not wrap in spinner.
