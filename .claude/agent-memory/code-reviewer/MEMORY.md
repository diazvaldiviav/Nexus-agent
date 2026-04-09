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

## Full-Codebase Antipattern Sweep (2026-03-16)
- KnowledgeGraph: 14 of 16 async methods missing CancellationToken (HIGH pattern across whole class)
- KnowledgeGraph: SqliteCommand not disposed (missing `using`) in AddEntityAsync, UpdateEntityAsync, AddRelationAsync, AddInteractionAsync, LogActionAsync, GetEntitiesWithEmbeddingsAsync, ApplyDecayAsync — same pattern repeated
- KnowledgeGraph: DateTime.Parse used without DateTimeStyles.RoundtripKind — silently wrong on round-trip of 'O'-format strings in some cultures
- KnowledgeGraph: not IDisposable itself; callers have no way to know connections are ephemeral — not a leak but worth noting
- DatabaseInitializer.Initialize() is synchronous despite SqliteConnection.Open() having async alternative
- AgentService: _conversationHistory shared mutable list — no thread-safety, ChatStreamAsync and ChatAsync race condition possible
- AgentService: background Task.Run lambda captures agentResponse after method returns — agentResponse.ExtractedEntities written from background thread, read by caller
- AgentService: CallOllamaAsync has no error handling around EnsureSuccessStatusCode; exceptions propagate to CallLlmAsync catch(Exception) — swallows structural errors with generic fallback message
- AgentService: hardcoded "http://localhost:11434" fallback in CallOllamaAsync and StreamOllamaAsync (line 244, 275)
- AgentService: static _httpClient field — correct pattern but note it uses 120s timeout while OllamaLlmClient uses 60s (inconsistency)
- MemoryContextBuilder: BuildContextAsync calls _graph.GetAllEntitiesAsync() and _graph.GetAllRelationsAsync() without CancellationToken; full-table scans, no pagination
- RelevanceDecay.ApplyDecayAsync: missing CancellationToken; N+1 UPDATE pattern (one UPDATE per entity instead of batch)
- McpClientManager.ConnectAsync: `new HttpClient()` per call — socket exhaustion antipattern (line 33)
- MainWindowViewModel: service-locator pattern via IServiceProvider — DIP violation; resolves ViewModels from container on demand
- MemoryGraphViewModel.OnSelectedEntityTypeChanged: fire-and-forget `_ = LoadGraphAsync()` — exceptions silently swallowed
- ActionLogViewModel.OnFilterTypeChanged: same fire-and-forget pattern
- GraphCanvas.Render: allocates new SolidColorBrush, Pen, FormattedText on every render frame — GC pressure per frame
- SettingsViewModel: hardcoded model strings ("qwen3:14b", "claude-sonnet-4-5-20250929") in ObservableCollection constructor

## Sprint 1 Day 3 — E2EFlowTests / DIFactoryTests Review (2026-03-13)
- Decision: APPROVED WITH SUGGESTIONS (3 MEDIUM, 3 LOW, 0 HIGH)
- MEDIUM-1: E2EFlowTests.BugFix001 calls SqliteConnection.ClearAllPools() inside a test method — move to Dispose() only
- MEDIUM-2: Flow1 asserts Embedding byte length as `768 * 4` hardcoded — should compute via SemanticSearch.ToByteArray(fakeEmbedding).Length
- MEDIUM-3: DIFactoryTests.DI_OpenAiProvider_NoApiKey_ThrowsDescriptiveError asserts on ex.Message directly; DI container may wrap factory exception, burying the message in InnerException
- LOW-1: FakeEmbeddingService Func<string,float[]> constructor overload is unused in current suite — either use or remove
- LOW-2: Flow2 asserts on WorkingMemory.Concat(RelevantMemory) — should assert specifically on RelevantMemory to pin the intent
- LOW-3: MockLlmClient duplicated between Integration.Tests.Fakes and EntityExtractorTests private class — future refactor candidate
- Good patterns: orthogonal unit vector setup for deterministic cosine similarity; named constructor args for RelevanceDecay; env-var save/restore in try/finally for CI safety
