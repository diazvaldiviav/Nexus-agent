# Code Reviewer — Persistent Memory

## Project Facts
- Runtime: .NET 10 (TFM net10.0), NuGet packages still at 9.0.0 (known debt)
- Solution: NexusAgent.slnx (5 src + 3 test projects)
- Skills to load before every review: coding-standards/SKILL.md, solid-principles/SKILL.md

## Architecture Patterns Confirmed
- Cross-layer interface pattern: interface in Nexus.Memory, implementation in Nexus.Core
  (IEmbeddingService/OllamaEmbeddingService, ILlmClient/OllamaLlmClient)
- No circular references: Memory has NO project references; Core -> Memory
- DI: all registrations in ServiceCollectionExtensions.AddNexusAgent()
- Concrete types used directly in DI lambdas (EntityExtractor, KnowledgeGraph) — pre-existing debt, not a new violation
- Hand-rolled mocks preferred over Moq/NSubstitute (single-method interfaces)
- File-based SQLite for tests: requires SqliteConnection.ClearAllPools() before File.Delete() in Dispose()

## Common Patterns to Watch For
- Static HttpClient for production; injected HttpClient for testability (OllamaLlmClient pattern)
- Optional constructor params (ILlmClient? logger?) used for backward compat — acceptable in this project
- ConfigureAwait(false) used consistently in OllamaEmbeddingService and OpenAiEmbeddingService; not enforced project-wide
- IsStopWord() in EntityExtractor allocates a new array on every call — known performance debt
- EmbeddingOptions endpoint default in ServiceCollectionExtensions hardcodes Ollama localhost even when provider=openai — known debt (flagged 2026-03-13, MEDIUM)
- Calling SqliteConnection.ClearAllPools() inside individual test methods (not just Dispose) is a footgun — it closes connections for all active tests in the run

## US-1.2 Review Notes (2026-03-06)
- entityMap uses StringComparer.OrdinalIgnoreCase — correct, better than design doc's .ToLower() suggestion
- Gemini fallback HttpClient created at DI registration time (not per-request) — acceptable
- OllamaLlmClient does NOT use static HttpClient (intentional — injected for testability, aligns with OllamaEmbeddingService pattern)
- TaskCanceledException in TryCloudFallbackAsync now correctly checks cancellationToken.IsCancellationRequested (FIXED from earlier debt)
- CreateRelationsAsync missing CancellationToken propagation in design doc — implementation correctly adds it

## OpenAI Embedding Provider Review (2026-03-13)
- OpenAiEmbeddingService follows OllamaEmbeddingService pattern exactly (injected HttpClient, same error handling shape)
- 401 and 429 handled as distinct status codes with actionable messages — improvement over OllamaEmbeddingService
- TaskCanceledException guard correctly implemented from the start (no debt)
- MockHandler in OpenAiEmbeddingServiceTests captures URI + headers + body — use this pattern for future HTTP tests
- MockHandler(Exception) constructor exists but no test exercises it — dead test infrastructure, flag in future reviews
- HttpStatusCode.TooManyRequests should be used instead of (HttpStatusCode)429 — LOW debt

## US-1.3 / US-1.4 Review Notes (2026-03-06)
- DI ordering bug: IEmbeddingService registered AFTER EntityExtractor and MemoryContextBuilder that depend on it — those use sp.GetService<IEmbeddingService>() (nullable), so it resolves null at runtime with standard DI
- MemoryContextBuilder.BuildContextAsync has no CancellationToken parameter — diverges from project async convention
- UpdateEntityAsync in KnowledgeGraph has no CancellationToken parameter on its public API
- FormatContextAsPrompt uses relation IDs (GUIDs) instead of entity names in output — low-value UX issue
- FakeEmbeddingService is not async-safe: if _exception is not null, it throws synchronously not via Task (acceptable for tests but note the pattern)
- ConfigureAwait(false) used consistently throughout MemoryContextBuilder and EntityExtractor (good)

## Sprint 2 Day 2 — AnthropicLlmProvider / OpenAiLlmProvider Review (2026-03-15)
- Decision: APPROVED WITH SUGGESTIONS (3 MEDIUM, 4 LOW, 0 HIGH)
- MEDIUM-1: AnthropicLlmProvider.BuildRequestBody is an instance method (reads _maxTokens); GeminiLlmProvider and OpenAiLlmProvider are static — make it `private static` and pass maxTokens as parameter
- MEDIUM-2: ServiceCollectionExtensions DI key resolution (lines 142-144 and 156-158) silently discards config.api_key when the cloud provider name doesn't match — add inline comments documenting intent
- MEDIUM-3: AnthropicLlmProvider has a 400 branch in ThrowOnErrorAsync with no test; both Anthropic and OpenAI miss a 429 test (AC-10 minimum of 3 met but new branches uncovered)
- LOW-1: Trailing blank line at AnthropicLlmProvider.cs:191 — cosmetic inconsistency
- LOW-2: nexus.yaml.example lists speculative gpt-5/gpt-5.2/gpt-5.4 model names — replace with known-good gpt-4o-mini, gpt-4o
- LOW-3: TestHttpMessageHandler is now triplicated across GeminiLlmProviderTests, AnthropicLlmProviderTests, OpenAiLlmProviderTests — move to Nexus.Core.Tests/Fakes/ in future sprint
- LOW-4: AgentService ChatAsync and ChatStreamAsync token count (chars/4) verified consistent across both paths — no issue
- Good patterns: Anthropic named-event SSE state machine correct; [DONE] check is strict string equality not Contains; ConfigureAwait(false) consistent; HttpCompletionOption.ResponseHeadersRead used in streaming; system as top-level field (not inside messages array)
- Established: DI provider key resolution pattern (ternary on provider name, then env var) is intentional and correct — document in code, not just tests

## Sprint 2 Day 2 (cont.) — ProviderKeyConfig / GetApiKey Review (2026-03-15)
- Decision: APPROVED WITH SUGGESTIONS (3 MEDIUM, 4 LOW, 0 HIGH)
- MEDIUM-1: [YamlMember(Alias = "openai")] on OpenAi property + UnderscoredNamingConvention serializer risk — YamlDotNet may write property back as `open_ai:` on Save, silently discarding user's `openai:` keys. Needs round-trip test or rename `OpenAi` -> `Openai` to let convention produce `openai` naturally.
- MEDIUM-2: ModelsConfigGetApiKeyTests covers tier-1 (dedicated section) and tier-2 (Cloud.ApiKey match) but has NO test for tier-3 (env-var fallback). Use try/finally save-restore pattern from Sprint 1 Day 3. Also: "google" alias in ResolveProvider has no test.
- MEDIUM-3: SaveSettings in SettingsViewModel is synchronous (calls File.WriteAllText via ConfigLoader.Save) — acceptable for small YAML file, but no Async suffix and no async upgrade path documented.
- LOW-1: nexus.yaml.example still lists gpt-5/gpt-5.2/gpt-5.4 speculative model names — flagged in Sprint 2 Day 2 review; still unresolved.
- LOW-2: GetEndpoint tested only for dedicated-section path; Cloud.Endpoint fallback and null-return paths not tested.
- LOW-3: ResolveProvider allocates new string[] on every call — convert env var arrays to static readonly fields.
- LOW-4: SettingsView.axaml API Keys grid uses inline Margin="0,8,0,0" for row spacing instead of consistent StackPanel Spacing pattern used elsewhere in the view.
- Good patterns: ResolveProvider as private helper centralizing the switch is clean SRP; "gemini" or "google" arm correctly aliases both names; ??= null-coalescing assignment on Save is idiomatic; PasswordChar="*" on all 3 API key fields correct.
- Risk to watch: YamlMember alias + UnderscoredNamingConvention interaction — verify before shipping.

## InteractionSummarizer Review (2026-03-18)
- Decision: APPROVED WITH SUGGESTIONS (3 MEDIUM, 6 LOW, 0 HIGH)
- MEDIUM-1/2: No IInteractionSummarizer interface — both InteractionSummarizer (Nexus.Memory) and AgentService (Nexus.Core) use concrete type; extract interface + register as singleton<IInteractionSummarizer> in next sprint
- MEDIUM-3: Double-fault path (LLM fails + fallback AddInteractionAsync also fails) has no test — add test that verifies method returns fallback Interaction without throwing
- LOW-1: `new List<string>()` on null-coalesce — replace with `[]` (C# 12)
- LOW-2: CleanSummary and GenerateHeuristicSummary are `internal static` — verify `[assembly: InternalsVisibleTo("Nexus.Memory.Tests")]` exists, or promote to `public static`
- LOW-3: _turnCount read inside Task.Run lambda without volatile/Interlocked — capture as local before Task.Run in both ChatAsync and ChatStreamAsync
- LOW-4: AddEntityAsync, UpdateEntityAsync, AddInteractionAsync still missing CancellationToken — carry-forward debt now more visible
- LOW-5: nexus.yaml.example speculative gpt-5 model names persist (3rd time flagged); comment block also incorrectly indented inside openai: stanza
- LOW-6: SummarizeAsync_WithLlm TokenCount assertion uses > 0 instead of pinning exact value (summary.Length / 4)
- Good patterns: two-level try/catch with heuristic fallback is correct; ConfigureAwait(false) consistent; CleanSummary empty-response guard correct; GetRecentInteractionsAsync and GetInteractionCountAsync correctly add CancellationToken
- New KnowledgeGraph methods (GetRecentInteractionsAsync, GetInteractionCountAsync, MapInteraction) are clean additions
- ClearHistoryAsync correctly summarizes before clearing + resets _turnCount to 0

## US-2.3 — GeminiEmbeddingService / FallbackEmbeddingService Tests Review (2026-03-18)
- Decision: APPROVED WITH SUGGESTIONS (4 MEDIUM, 4 LOW, 0 HIGH). ACs met (5 + 5 tests, both > 3 minimum).
- MEDIUM-1: GeminiEmbeddingServiceTests missing network failure test (HttpRequestException path). MockHandler(Exception) constructor exists but is never used — same dead infrastructure pattern as OpenAI tests. Fix with GenerateEmbeddingAsync_NetworkDown test.
- MEDIUM-2: Constructor_MissingApiKey packs null/empty/whitespace into a single [Fact] — three Assert.Throws in one method. Same pattern carried from OpenAiEmbeddingServiceTests (pre-existing debt). Split into [Theory]/[InlineData] or three [Fact]s.
- MEDIUM-3: HttpStatusCode.Forbidden (403) not tested in GeminiEmbeddingServiceTests — source code checks Unauthorized OR Forbidden as one branch. Only 401 covered. A narrowing regression would pass all tests.
- MEDIUM-4: FallbackEmbeddingServiceTests has no CancellationToken forwarding test — FallbackEmbeddingService passes token to both primary and fallback, but FakeEmbeddingService ignores it.
- LOW-1: GeminiEmbeddingServiceTests — LastRequestBody is captured by MockHandler but never asserted (compare: OpenAiEmbeddingServiceTests asserts body contains model name).
- LOW-2: GeminiEmbeddingServiceTests — MockHandler(Exception) dead until MEDIUM-1 fixed.
- LOW-3: FallbackEmbeddingServiceTests BothFail test asserts on message string but not exception type.
- LOW-4: FallbackEmbeddingServiceTests — logger injection path (_logger?.LogWarning on fallback) not verified in any test. Low priority per team convention.
- Good patterns: GeminiEmbeddingServiceTests asserts URL contains model name, API key, and embedContent — strong HTTP contract pinning. FallbackEmbeddingServiceTests uses CallCount from FakeEmbeddingService precisely. PrimaryFailsNoFallback edge case correctly tested.
- Note: FakeEmbeddingService throws synchronously (not via Task) when exception is set — acceptable for tests, noted as pre-existing pattern.

## Sprint 2 Day 3 (cont.) — FakeLlmProvider / CloudFlowTests / Program.cs stats Review (2026-03-18)
- Decision: APPROVED WITH SUGGESTIONS (5 MEDIUM, 5 LOW, 0 HIGH). ACs met: US-2.2 AC-7, US-2.3 AC-5, US-2.3 AC-6.
- MEDIUM-1: FakeLlmProvider.cs:37 — `await Task.CompletedTask` after last `yield return` is unreachable dead code; remove it.
- MEDIUM-2: FakeLlmProvider.cs:11 — constructor does not null-guard `providerName` or `responseFactory`; add ArgumentNullException guards per project pattern.
- MEDIUM-3: CloudFlowTests Flow4 context assertion is non-deterministic (`Assert.NotEmpty(allMemory)` only); pin to specific entity names (Alice or Nexus) as Flow2 does with CSharp.
- MEDIUM-4: CloudFlowTests Flow5 does not fetch-and-compare the persisted interaction's summary from the DB — count==1 passes even if AddInteractionAsync silently discards data. Add GetRecentInteractionsAsync fetch + summary assertion.
- MEDIUM-5: Program.cs stats label reads "Interactions:" — AC-7 says "interaction/summary count"; rename to "Interaction summaries:" for clarity since summaries are stored as Interaction rows.
- LOW-1: FakeLlmProvider has no CallCount/LastPrompt tracking — asymmetry with MockLlmClient pattern; add for future test hooks.
- LOW-2: CloudFlowTests Flow5 — locally constructed `search` and `builder` inside the test method are not disposed; inconsistent with E2EFlowTests class-level pattern.
- LOW-3: Program.cs:205 — `await Task.CompletedTask` at end of RunMemoryCommandAsync is dead code; remove.
- LOW-4: Flow4 entity assertion for "Nexus" depends on heuristic correctly extracting it from the LLM response string — if heuristic changes, test breaks unexpectedly; worth a comment.
- LOW-5: CloudFlowTests uses `MockLlmClient` directly (not `ILlmClient`) — consistent with existing pattern but interface would communicate intent better.
- Good patterns: FakeLlmProvider correctly applies [EnumeratorCancellation] on CancellationToken; both tests use GUID-named temp DB with ClearAllPools() in Dispose(); test naming follows Method_Scenario_Expected convention; Flow5 is a true end-to-end integration (no mocked persistence layer).
- Established: `await Task.CompletedTask` as last line of async iterator or async method is a recurring anti-pattern — flag proactively in future reviews.

## Sprint 2 Day 4 (cont.) — Program.cs single-query / pipe / exit-code / help Review (2026-03-19)
- Decision: APPROVED WITH SUGGESTIONS (3 MEDIUM, 5 LOW, 0 HIGH). ACs 1-6 met.
- MEDIUM-1: FlushPendingExtractionAsync failure inside try-catch on line 87 returns exit code 1 correctly (AC-5), but AggregateException from background task produces generic "One or more errors occurred." to stderr — unwrap InnerException before writing to Console.Error.
- MEDIUM-2: Piped stdin + inline args precedence undocumented — `echo "q1" | nexus "q2"` silently discards stdin with no warning. Add comment at line 23 documenting that inline args take precedence over piped stdin.
- MEDIUM-3: `await Task.CompletedTask` dead code on line 265 of RunMemoryCommandAsync still present (flagged as LOW-3 in Sprint 2 Day 3 review, now re-elevated to MEDIUM as 4th occurrence); remove unconditionally.
- LOW-1: `.ToLower()` without culture on lines 116,123 — use string.Equals(..., StringComparison.OrdinalIgnoreCase)
- LOW-2: Version string "v1.0.0-mvp" hardcoded on line 47 — derive from Assembly or a shared constant
- LOW-3: ConfigLoader.Load on line 11 called before any try/catch — config-missing exception produces unformatted stack trace, not a clean `Error: ...` on stderr; wrapping would complete AC-5 for the config-missing case
- LOW-4: decay.ApplyDecayAsync() on line 73 is outside the try block — SQLite failure here leaks stack trace instead of clean exit code 1
- LOW-5: ShowHelp FigletText banner (line 312) appears verbatim in pipe output (`nexus --help | grep`) — suppress when Console.IsOutputRedirected
- Good patterns: Console.Write(token) with no buffering correct for AC-4 streaming; Console.Error.WriteLineAsync on line 92 correctly separates error stream; newline suppression on line 83 (IsOutputRedirected check) is correct AC-6 behavior; FilterConfigArgs is a clean pure static method; `return await RunSingleQueryAsync(...)` on line 27 correctly propagates exit code from piped stdin path.
- Confirmed: `return await RunSingleQueryAsync(sp, query.Trim())` on line 27 propagates exit code correctly — callers must use `return` not `await` alone.

## Sprint 2 Day 5 (cont.) — Fakes Extraction / New Tests Review (2026-03-19)
- Decision: APPROVED WITH SUGGESTIONS (2 MEDIUM, 4 LOW, 0 HIGH). All 5 ACs met.
- MEDIUM-1: MockLlmClient still duplicated — Nexus.Memory.Tests/Fakes/ (new) vs Nexus.Integration.Tests/Fakes/ (pre-existing); the two differ only in `sealed` modifier. Deduplicate in a future sprint.
- MEDIUM-2: GeminiEmbeddingServiceTests still retains its own private MockHandler class even after TestHttpMessageHandler was extracted to Nexus.Core.Tests/Fakes/. MockHandler is local to the Memory.Tests project, which cannot reference Core.Tests; extraction is valid, but the name collision (MockHandler vs TestHttpMessageHandler) across test projects is confusing.
- LOW-1: TestHttpMessageHandler has no null-guard on constructor args (no ArgumentNullException for null responseContent). Minor but inconsistent with project constructor pattern.
- LOW-2: ForceDirectedLayoutTests.Step_EmptyNodeList_DoesNotThrow asserts `result >= 0` — the assertion value (0) is a magic number; prefer `Assert.Equal(temperature, result)` to pin that an empty list returns the unchanged temperature.
- LOW-3: FallbackEmbeddingService CancellationToken test only covers primary path; no test for token forwarding when primary fails and fallback is invoked.
- LOW-4: GeminiEmbeddingServiceTests.MockHandler(Exception) constructor still dead — MEDIUM-1 from US-2.3 review unresolved after adding Forbidden test.
- Good patterns: TestHttpMessageHandler is clean, simple, captures LastRequest — correct level of abstraction for the 4 provider test classes. MockLlmClient exposes LastPrompt correctly. FakeEmbeddingService.LastCancellationToken pattern is clean for CT forwarding tests. ForceDirectedLayoutTests naming follows Method_Scenario_Expected convention.
- Established: When a Fakes class is extracted to a test project's Fakes/ folder, it must be usable by all test classes in that project — verify namespace is consistent (Nexus.Core.Tests.Fakes is correct for Core tests; Nexus.Memory.Tests.Fakes is correct for Memory tests).

## Sprint 2 Day 5 (fixes) — ToolRegistry / Program.cs / nexus.yaml.example / MemoryGraphViewModel / MemoryGraphView Review (2026-03-19)
- Decision: APPROVED WITH SUGGESTIONS (2 MEDIUM, 3 LOW, 0 HIGH). All 6 ACs met.
- AC-2.1 (ToolRegistry thread safety): PASS — ConcurrentDictionary, TryRemove, snapshot (ToList()) before iteration in UnregisterToolsForServer. One residual race: snapshot between LINQ filter and TryRemove could let a concurrent RegisterTool re-add a key that is then removed — acceptable given the documented "brief window" comment.
- AC-2.2 (dead code removal): PASS — `await Task.CompletedTask` is fully removed from Program.cs. 0 occurrences confirmed.
- AC-2.3 (OrdinalIgnoreCase): PASS — RunChatAsync "exit"/"clear" comparisons now use string.Equals(..., StringComparison.OrdinalIgnoreCase). All routing uses == on filteredArgs[0] string literals which is culture-insensitive. Memory command routing also uses == literals.
- AC-2.4 (AggregateException unwrap): PASS — Line 92 correctly unwraps: `ex is AggregateException agg ? agg.InnerException?.Message ?? ex.Message : ex.Message`.
- AC-2.5 (nexus.yaml.example model names): PASS — openai stanza now shows `gpt-4o-mini (cheapest), gpt-4o, o1-mini`. Speculative gpt-5 names are gone. Comment indentation also fixed.
- AC-2.6 (Select All / Clear All): PASS — SelectAllFilters and ClearAllFilters [RelayCommand] methods exist in ViewModel; AXAML binds to SelectAllFiltersCommand / ClearAllFiltersCommand with Padding and FontSize.
- MEDIUM-1: ToolRegistry.UnregisterToolsForServer race — snapshot (ToList) is taken from a live LINQ enumeration over _tools; a concurrent write between snapshot and TryRemove could cause silent no-op removal. Acceptable in current usage (single-threaded reconnect), but add XML doc note that callers must serialise reconnect calls, or use a lock for true thread-safety.
- MEDIUM-2: AggregateException unwrap (Program.cs:92) only unwraps one level — nested AggregateException (e.g., Task.WhenAll) would still expose "One or more errors occurred." Use `ex.Flatten().InnerException` for full unwrap.
- LOW-1: decay.ApplyDecayAsync() on line 73 is still outside the try block — SQLite failure here leaks stack trace instead of returning exit code 1 (carry-forward from Sprint 2 Day 4 review).
- LOW-2: Version string "v1.0.0-mvp" on line 47 still hardcoded — derive from Assembly (carry-forward).
- LOW-3: MemoryGraphView.axaml filter row button padding (Padding="6,2") is inline magic; consider extracting to a shared Style in App.axaml for consistency with other small buttons.
- Good patterns: ToolRegistry XML doc on RegisterToolsFromServer correctly documents the "brief window" risk — proactive disclosure is excellent. SelectAllFilters/ClearAllFilters correctly call ApplyFilterAndLayout() after toggling. EntityTypeFilter.IsSelected PropertyChanged subscription correctly triggers re-filter on individual checkbox change.
- Carry-forward debt still open: decay.ApplyDecayAsync outside try block (Program.cs), version string hardcode, ToolRegistry reconnect serialization note missing.

## MemoryCompressor Re-Review (2026-03-25)
- Decision: APPROVED — all HIGH and MEDIUM issues from initial review are FIXED; 0 new issues introduced
- HIGH-1 FIXED: Dispose() now calls Directory.Delete(_archiveDir, recursive: true) correctly
- HIGH-2 FIXED: IKnowledgeGraph.GetRelationsForEntityAsync now takes CancellationToken; KnowledgeGraph passes it through OpenAsync, ExecuteReaderAsync, and ReadAsync; MemoryCompressor passes ct at call site
- MEDIUM-1 FIXED: ThrowingKnowledgeGraph stub added inline — implements all 17 IKnowledgeGraph methods; ArchiveStaleEntities_GraphThrows_ReturnsZero test covers never-throws contract
- MEDIUM-2 FIXED: DeleteFailingKnowledgeGraph wrapper added inline — delegates all methods, throws on DeleteEntityAsync for a specific entity; ArchiveStaleEntities_DeleteThrowsForOneEntity_StillCompletesArchive test verifies both entities still archived
- MEDIUM-3 FIXED: JsonOptions is now `internal static` on MemoryCompressor; duplicate local declaration removed from tests; tests use MemoryCompressor.JsonOptions directly
- LOW-2 FIXED: ArchiveModels.cs uses collection expressions `[]` instead of `new List<>()`
- Note: EntityExtractor still calls GetRelationsForEntityAsync(entity1.Id) without passing CancellationToken — carry-forward pre-existing debt (acceptable, parameter defaults to default)
- Good patterns: stub classes correctly sealed + internal; DeleteFailingKnowledgeGraph correctly delegates CancellationToken on all passthrough methods; Dispose() catches Directory.Delete exception silently (correct — archive dir may not exist); 163 tests all passing

## Sprint 3 Day 7 — OnboardingWizard / ConfigLoader.Exists / Program.cs Review (2026-03-30)
- Decision: APPROVED WITH SUGGESTIONS (3 MEDIUM, 5 LOW, 0 HIGH). All 10 ACs met.
- MEDIUM-1: DetectOllamaAsync mixes AnsiConsole.Status() spinner with HTTP+parse logic — tests call the method directly but Spectre spinner may throw/corrupt in CI/headless. Extract HTTP+parse into a private ParseOllamaResponseAsync helper; tests call that instead.
- MEDIUM-2: Line 27 `new HttpClient { Timeout=10s }` created when httpClient param is null — not disposed, leaks if RunAsync called twice. Use `using var localHttp = httpClient is null ? new HttpClient{…} : null; var http = httpClient ?? localHttp!;`
- MEDIUM-3: ConfigLoader.Exists uses `||` while Load uses null-coalescing chain — functionally equivalent but fragile if either is changed in isolation. Add comment on Exists() that search order must mirror Load().
- LOW-1: Two `new List<string>()` in DetectOllamaAsync — replace with `[]` (C# 12 collection expression, project convention)
- LOW-2: TestHttpMessageHandler duplicated in Core.Tests/Fakes and Integration.Tests/Fakes — architecturally forced (test projects cannot cross-reference); add comment in each file to prevent misguided dedup attempts
- LOW-3: T-7 uses HttpStatusCode.InternalServerError to simulate "Ollama not running" — real scenario is connection refused (HttpRequestException with inner SocketException); add a companion test using the Exception constructor of TestHttpMessageHandler
- LOW-4: Version string "v1.0.0-mvp" still hardcoded (3rd time flagged) — carry-forward
- LOW-5: decay.ApplyDecayAsync() outside try block in RunSingleQueryAsync — carry-forward
- Good: Console.IsInputRedirected guard at top of RunAsync; top-level catch returns default NexusConfig; RecommendChatModel/RecommendEmbeddingModel read POCO defaults not hardcoded strings; per-step typed catches; GenerateConfig omits cloud sections for skipped keys; ConfigLoader.Exists mirrors Load() search path; Phase 2 before Phase 3 prevents wizard on `nexus help`
- Established: Spectre.Console Status() spinner inside internally-tested methods is a recurring anti-pattern — extract logic into pure helper, test the helper.

## ContextWindowManager Review (2026-04-06)
- Decision: APPROVED WITH SUGGESTIONS (2 MEDIUM, 3 LOW, 0 HIGH)
- AC-1 through AC-6 all satisfied
- MEDIUM-1: ContextWindowManager.cs:30 — `systemPrompt?.Length ?? 0` uses null-conditional on a non-nullable `string` param; remove guard or annotate as `string?`
- MEDIUM-2: ContextWindowManager.cs:62 — magic string `"system"` role and `"[Conversation Summary]\n"` prefix hardcoded inline; no named constants
- LOW-1: ContextWindowManagerTests.cs:97 — `Assert.Equal(5, ...)` assumes summarizer succeeds; depends on shared stub default, not per-test isolation
- LOW-2: CompactIfNeededAsync returns `true` even when only truncation (no summary) occurs; semantics are correct but a comment would help
- LOW-3: StubInteractionSummarizer throws synchronously (not via Task.FromException) — consistent with project pattern, acceptable
- Good: ConfigureAwait(false) on all awaits; null guards in constructor; fallback-to-truncation pattern; 8 well-structured tests with clear AAA; StubKnowledgeGraph fully implements interface

## Sprint 1 Day 3 — E2EFlowTests / DIFactoryTests Review (2026-03-13)
- Decision: APPROVED WITH SUGGESTIONS (3 MEDIUM, 3 LOW, 0 HIGH)
- MEDIUM-1: E2EFlowTests.BugFix001 calls SqliteConnection.ClearAllPools() inside a test method — move to Dispose() only
- MEDIUM-2: Flow1 asserts Embedding byte length as `768 * 4` hardcoded — should compute via SemanticSearch.ToByteArray(fakeEmbedding).Length
- MEDIUM-3: DIFactoryTests.DI_OpenAiProvider_NoApiKey_ThrowsDescriptiveError asserts on ex.Message directly; DI container may wrap factory exception, burying the message in InnerException
- LOW-1: FakeEmbeddingService Func<string,float[]> constructor overload is unused in current suite — either use or remove
- LOW-2: Flow2 asserts on WorkingMemory.Concat(RelevantMemory) — should assert specifically on RelevantMemory to pin the intent
- LOW-3: MockLlmClient duplicated between Integration.Tests.Fakes and EntityExtractorTests private class — future refactor candidate
- Good patterns: orthogonal unit vector setup for deterministic cosine similarity; named constructor args for RelevanceDecay; env-var save/restore in try/finally for CI safety

## Hardware Sprint 1 Day 3 — ISensorMonitor / HostStateClassifier / Boundary Tests (2026-04-04)
- Decision: APPROVED (0 HIGH, 0 MEDIUM, 2 LOW)
- LOW-1: MakeGpuEnvelope(-1L) passes negative value as UsableLocalVramNow — semantically odd (valid vram can't be negative) but GpuEnvelope has no validation and classifier reads SafeGpuBudget only; functionally correct, worth a comment
- LOW-2: ClassifyGpu switch uses `<= 0` for None arm; ClassifyCpu/ClassifyRam use strict `<` — deliberate (GPU None must catch exactly-zero), but worth an XML comment on the None arm to prevent future "alignment" bugs
- AC-14 verified: 362 tests total (1+62+70+76+103+50), 0 failures, 0 build errors (1 Avalonia warning is pre-existing)
- All 8 threshold constants match AC-9 exactly; all boundary values (at-threshold, just-below, just-above) covered by 8+8+8=24 InlineData entries
- Good patterns: pure static classifier with no dependencies; switch expression is idiomatic and exhaustive; MakeXxxEnvelope private static helpers keep test bodies clean (3 lines each); ISP correct — ISensorMonitor empty marker interface separated cleanly for Sprint 2 placeholder

## Sprint 4 Day 7 — WindowsHostProfiler / WindowsHostProfilerTests (2026-04-06)
- See: [sprint4-day7-windowshostprofiler-review.md](sprint4-day7-windowshostprofiler-review.md)
- Decision: CHANGES REQUIRED (1 HIGH, 3 MEDIUM, 4 LOW)
- HIGH-1: AllSucceed asserts CpuState.Strong for CpuInferenceScore=0.75 — classifier is `< 0.75 => Strong` (strict), so 0.75 maps to HighEnd. Broken assertion. Fix ValidCpu score to 0.70 or change assertion to HighEnd.
- MEDIUM-3: `.Result` after Task.WhenAll (lines 46-48) violates coding-standards SKILL "NEVER .Result" — safe here (tasks complete) but should use `(await cpuTask, await ramTask, await gpuTask)`
- LOW-2: Missing `[SupportedOSPlatform("windows")]` on WindowsHostProfiler class (hardware-engineering SKILL ADR)
- Pattern: static property vs static readonly for test fixtures — recurring issue (3rd time); standardise on `static readonly`

## Sprint 4 Day 6 — ChatView bubble + auto-scroll (2026-04-04)
- See: [sprint4-day6-chatview-scroll-review.md](sprint4-day6-chatview-scroll-review.md)
- Decision: APPROVED WITH SUGGESTIONS (0 HIGH, 3 MEDIUM, 3 LOW)
- MEDIUM-2 is highest risk: OnScrollChanged programmatic-scroll race disables _autoScrollEnabled right after ScrollToBottom() posts — fix with _isProgrammaticScroll flag
- MEDIUM-1: DataContext-change event leak in OnLoaded/OnUnloaded — fix with DataContextChanged subscription
- MEDIUM-3: MaxWidth="600" hardcoded pixels, not 75% of panel — AC-2 partial fail
- Established: _isProgrammaticScroll flag is the correct Avalonia idiom for separating user-initiated from programmatic ScrollChanged events

## Sprint 4 Day 8 — LHM + PerfCounter Monitors (US-2.5 / US-2.6) (2026-04-07)
- See: [sprint4-day8-lhm-perf-monitor-review.md](sprint4-day8-lhm-perf-monitor-review.md)
- Decision: APPROVED WITH SUGGESTIONS (0 HIGH, 4 MEDIUM, 4 LOW)
- MEDIUM-1: [SupportedOSPlatform("windows")] missing on LhmComputerWrapper, LhmSensorMonitor, PerfCounterProvider, PerfCounterMonitor — WmiCpuProfiler has it, inconsistency
- MEDIUM-2: LhmSensorMonitorTests.Dispose_DisposesComputer uses Assert.True(true) — vacuous assertion; FakeLhmComputer not IDisposable so disposal never verified
- MEDIUM-3: SelectPreferred null-struct sentinel (preferred.SensorName is not null) relies on default struct having null string field — fragile; use Any() + predicate overload instead
- MEDIUM-4: FakeLhmComputer missing IDisposable — add it (mirrors FakePerfCounterProvider pattern)
- LOW-1: PerfCounterProvider.SafeRead uses bare `catch {}` — change to `catch (Exception)`
- Good: hardware.Update() + sub.Update() correct; IsMemoryEnabled=false; three separate try/catch in PerfCounterMonitor; FakePerfCounterProvider.Disposed correctly verified; ConfigureAwait(false) consistent
- Established: Assert.True(true, "...") as placeholder is a recurring anti-pattern — flag immediately in future reviews
