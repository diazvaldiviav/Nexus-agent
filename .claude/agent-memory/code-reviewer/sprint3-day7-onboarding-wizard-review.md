---
name: Sprint 3 Day 7 — OnboardingWizard / ConfigLoader.Exists / Program.cs restructure Review
description: Review of onboarding wizard (AC-1 to AC-10), ConfigLoader.Exists, 5-phase Program.cs, and 7 onboarding tests
type: project
---

Decision: APPROVED WITH SUGGESTIONS (3 MEDIUM, 5 LOW, 0 HIGH). All 10 ACs met.

## Key findings

### HIGH — none

### MEDIUM
- MEDIUM-1: DetectOllamaAsync calls AnsiConsole.Status().StartAsync() — but T-6 and T-7 tests call it directly. Spectre.Console's Status() spinner works on a live console; in a CI/headless environment it may throw or render ANSI escape sequences to stdout, polluting test output. The method mixes I/O side-effects (spinner) with logic (HTTP + parse). Extract the HTTP+parse logic into a private static ParseOllamaResponseAsync helper; DetectOllamaAsync remains the public-facing method with the spinner. Tests should call the parse helper, not DetectOllamaAsync.
- MEDIUM-2: OnboardingWizard.cs line 27 — `var http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) }`. When httpClient is null a new HttpClient is allocated per RunAsync() call with no disposal. This leaks sockets if RunAsync is called more than once (e.g., auto-trigger path followed by explicit `nexus init`). Either accept the singleton pattern (pass a static field) or add `using` around the locally created client only: `using var localHttp = httpClient is null ? new HttpClient { … } : null; var http = httpClient ?? localHttp!;`
- MEDIUM-3: ConfigLoader.Exists(configPath: null) returns true if EITHER DefaultConfigPath OR "nexus.yaml" (cwd) exists. ConfigLoader.Load(configPath: null) prefers DefaultConfigPath, then falls back to "nexus.yaml". The two methods are consistent in search order, but Exists() uses || while Load() uses a sequential null-coalescing chain. If only "nexus.yaml" (cwd) exists, Exists() returns true and Load() correctly loads it — but Program.cs Phase 3 adds no comment explaining this subtlety. A future developer changing either method in isolation could introduce a silent mismatch. Add an inline comment on Exists() noting that the search order must mirror Load().

### LOW
- LOW-1: OnboardingWizard.cs line 131 — `new List<string>()` returned on null-coalesce. Project convention (established Sprint 2 Day 3 review) is C# 12 collection expression `[]`. Replace both occurrences (lines 131 and 145).
- LOW-2: TestHttpMessageHandler is now duplicated across Nexus.Core.Tests/Fakes/ and Nexus.Integration.Tests/Fakes/ — identical implementations in different namespaces. This is the same pattern flagged as MEDIUM-1 in Sprint 2 Day 5 review for MockLlmClient. Integration.Tests now has a direct ProjectReference to Nexus.CLI (via csproj), not to Nexus.Core.Tests; the duplication is architecturally forced (test projects cannot reference each other). Document this in a comment in each file so future reviewers do not attempt a "dedup" that would create an illegal cross-test-project reference.
- LOW-3: T-7 (DetectOllama_WhenNotRunning_ReturnsEmptyList) passes HttpStatusCode.InternalServerError to TestHttpMessageHandler. DetectOllamaAsync catches HttpRequestException (thrown by EnsureSuccessStatusCode). This works correctly, but the test name says "not running" — a connection-refused scenario would throw HttpRequestException with a different inner cause, while a 500 is a server error. Consider adding a second variant that uses the Exception constructor of TestHttpMessageHandler to simulate connection refused, which is the real "Ollama not running" scenario.
- LOW-4: Version string "v1.0.0-mvp" on Program.cs line 29 remains hardcoded — carry-forward from Sprint 2 Day 4 and Sprint 2 Day 5 reviews. Third time flagged.
- LOW-5: decay.ApplyDecayAsync() on Program.cs line 105 is still outside the try block in RunSingleQueryAsync — SQLite failure leaks stack trace instead of returning exit code 1. Carry-forward from Sprint 2 Day 5 review.

## Good patterns
- Console.IsInputRedirected guard at top of RunAsync() is correct (AC requirement met).
- Top-level catch in RunAsync() correctly prints message and returns default NexusConfig — no crash.
- RecommendChatModel() / RecommendEmbeddingModel() instantiate config POCOs to read defaults — not hardcoded strings. Correct per architecture requirement.
- Per-step typed catches (HttpRequestException, TaskCanceledException, Win32Exception, InvalidOperationException, IOException, UnauthorizedAccessException, SqliteException) — no bare catch{}.
- GenerateConfig correctly sets only the cloud provider sections that have keys (no empty ProviderKeyConfig objects created for skipped providers).
- ConfigLoader.Exists() correctly mirrors Load() search path: explicit path → ~/.nexus/nexus.yaml → ./nexus.yaml.
- Phase 2 (early exits for init/help/version) correctly runs BEFORE Phase 3 (ConfigLoader.Exists check) — wizard cannot auto-trigger when user explicitly types `nexus help`.
- InternalsVisibleTo in Nexus.CLI.csproj is correctly scoped to Nexus.Integration.Tests only.
- T-4 and T-5 pin GenerateConfig outputs concretely — good regression coverage for the config shape.

## Established patterns
- When a test project adds a ProjectReference to a src project (Nexus.CLI), duplicate Fakes are architecturally unavoidable — do not flag as debt unless a shared test library is introduced.
- Spectre.Console Status() spinner inside internal methods tested directly is a recurring anti-pattern: methods that mix terminal I/O with testable logic should extract the logic into a pure helper.
