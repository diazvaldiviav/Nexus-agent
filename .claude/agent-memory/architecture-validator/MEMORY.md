# Architecture Validator Memory

## Project Quick Facts
- Solution: NexusAgent.slnx (5 src projects + 3 test projects)
- Layer order: Nexus.Interface -> Nexus.Core -> Nexus.Memory + Nexus.Connectors -> Nexus.CLI + Nexus.Desktop
- Nexus.Memory must NOT reference Nexus.Core (dependency flow rule, enforced via EmbeddingOptions pattern)
- All services registered in: src/Nexus.Core/ServiceCollectionExtensions.cs
- NexusConfig location: src/Nexus.Core/Config/NexusConfig.cs — EmbeddingsConfig already exists with Provider, Model, Endpoint (nullable), Dimensions
- ModelRouter: has IsLocal(TaskType) and IsCloud(TaskType) methods — confirmed in src/Nexus.Core/ModelRouter.cs

## Recurring Issues to Watch

### High-Priority Pattern: Exception Type Conflicts Between Docs
- When requirements and architecture docs define error handling tables separately, they often disagree on exception types.
- Example (US-1.6/US-1.1): Requirements says HttpRequestException, Architecture says InvalidOperationException wrapping it.
- Always flag this HIGH — unit tests cannot pass without knowing which exception type to assert.
- Fix: Architecture doc must be authoritative on exception types; requirements can describe the scenario.

### High-Priority Pattern: Constructor Signature Divergence Between Docs
- Requirements and architecture docs can define different constructor signatures for modified services.
- Example (US-1.2): Requirements says EntityExtractor(..., ILogger? logger), Architecture says EntityExtractor(..., HttpClient? geminiHttp, string? geminiApiKey).
- Always flag this HIGH — implementer cannot write stable code without resolution.
- Always check: is ILogger included? Are Gemini/fallback deps wired? Does the DI factory match the constructor?
- Fix: Architecture doc is authoritative; note discrepancy and fix constructor + DI factory together.

### High-Priority Pattern: Missing ILogger When Error Handling Strategy Says "Log"
- If error handling section says "log warning" at fallback paths but constructor omits ILogger, flag HIGH.
- Silent fallback chains (bare catch {}) are invisible in production and violate coding standards.
- Coding standards: bare `catch {}` is explicitly prohibited. Always require typed catches with logger calls.

### HttpClient Static vs Constructor-Injected Pattern
- NFR-4 says static/singleton HttpClient; testability requires constructor injection.
- Established resolution pattern: constructor accepts `HttpClient? httpClient = null`, defaults to `new HttpClient()` at construction time.
- Since service is Singleton, the per-instance HttpClient is effectively singleton — acceptable but not strictly static readonly.
- If both patterns appear in same codebase, flag as MEDIUM inconsistency.
- OllamaEmbeddingService is the reference implementation: `_http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) }`.

### Cross-Layer Config Access Pattern
- Memory layer cannot reference Core layer config (EmbeddingsConfig).
- Established pattern: create lightweight `EmbeddingOptions` record in Memory layer.
- DI registration in Core maps EmbeddingsConfig -> EmbeddingOptions with null-coalescing defaults.
- For ILlmClient (US-1.2): ILlmClient interface in Memory, OllamaLlmClient in Core — same pattern as IEmbeddingService.

### DI Registration Order in ServiceCollectionExtensions.cs
- Current order (post US-1.2): EmbeddingOptions, IEmbeddingService, KnowledgeGraph, SemanticSearch, MemoryContextBuilder, PromptBuilder, ModelRouter, ILlmClient, EntityExtractor, AgentService, RelevanceDecay.
- EntityExtractor is registered AFTER ModelRouter — confirmed correct in Sprint 1 Day 4 codebase scan.
- When adding services that depend on ModelRouter, always check existing registration order and reorder if needed.

### Environment Variable Isolation in DI Factory Tests
- DI factory tests that verify "missing API key throws" will silently pass if the developer's env has the key set (e.g., OPENAI_API_KEY).
- This risk applies to any DI "no key" test where the factory uses Environment.GetEnvironmentVariable.
- Fix: Either (a) test the constructor directly (bypasses env var lookup in factory), or (b) save/clear/restore the env var in the test using try/finally.
- Pattern (a) is preferred — simpler and tests the actual throw path without env side effects.

## Validated Designs
- US-1.6 + US-1.1: APPROVED WITH CONDITIONS (1 HIGH issue on exception type conflict, 2 MEDIUM)
  - File: docs/architecture/US-1.6-US-1.1-design.md
- US-1.2: APPROVED (Round 2 — all 5 previous issues fixed)
  - File: docs/architecture/US-1.2-design.md
  - Validation: docs/validation/US-1.2-validation.md
  - 2 LOW items remain as implementation-time reminders (DI registration order; GetService vs GetRequiredService for ILlmClient)
  - Codebase fact: Entity.Id is string (GUID), not long — Relation.EntityId1/2 are also string
- Sprint 1 Day 4 (US-1.5 + US-1.7 + Hotfixes): APPROVED (0 HIGH, 2 MEDIUM, 3 LOW)
  - File: docs/architecture/sprint-1-day-4-design.md
  - MEDIUM-1: DIFactoryTests "no key" test will false-green if OPENAI_API_KEY is set in env — isolate with save/clear/restore or test constructor directly
  - MEDIUM-2: BugFix001 test only verifies empty start state (already covered by AgentIntegrationTests) — clarify coverage rationale
  - LOW-2: No YAML round-trip test for EmbeddingsConfig.ApiKey — add to ConfigLoaderTests (5-line addition)
  - Codebase confirmed: UnderscoredNamingConvention in ConfigLoader ensures api_key -> ApiKey mapping works
  - Codebase confirmed: EmbeddingsConfig does NOT yet have ApiKey — must be added in step 1 of implementation
- US-2.1 Day 1 (ILlmProvider + OllamaLlmProvider + GeminiLlmProvider + LlmProviderFactory + AgentService refactor): NEEDS REVISION (1 HIGH, 2 MEDIUM, 2 LOW)
  - File: docs/architecture/US-2.1-day1-design.md
  - HIGH-1: GeminiLlmProvider error handling says "Log" for 401/403/429 but constructor has no ILogger parameter — silent errors in production
  - MEDIUM-1: conversationHistory semantic ambiguity — providers receive history but spec doesn't clarify if current user message is included or excluded (BuildOllamaMessages currently uses SkipLast(1))
  - MEDIUM-2: E2EFlowTests.cs line 249 constructs AgentService with 5 args — will break after constructor adds LlmProviderFactory (architecture acknowledges this in Risk #6 but fix is deferred to step 9)
  - LOW-1: GeminiLlmProvider DI registration uses conditional (only if API key present) — no test coverage for the "no key" registration path
  - LOW-2: LlmProviderFactory not exposed as interface (ILlmProviderFactory) — reduces testability of AgentService in unit tests
  - Codebase confirmed: ModelProviderConfig.ApiKey exists on NexusConfig — no config changes needed
  - Codebase confirmed: GEMINI_API_KEY env var pattern already used in ServiceCollectionExtensions for embeddings

- US-2.1 Day 2 (AnthropicLlmProvider + OpenAiLlmProvider + DI + token counting): APPROVED Round 2 (0 HIGH, 0 MEDIUM, 3 LOW)
  - File: docs/architecture/US-2.1-day2-design.md (provided inline by architect)
  - All 4 Round 1 issues resolved: JsonException handled; API key guard correct; Anthropic SSE two-variable loop specified; 3 tests per provider (2 happy + 1 error)
  - LOW-1 (carry-forward): ConfigureAwait(false) not in architecture doc — implementer must apply to all awaits in both providers (match GeminiLlmProvider/OllamaLlmProvider pattern)
  - LOW-2 (carry-forward): Missing 429 rate-limit test per provider — add ChatAsync_ThrowsOnRateLimit_When429Returned for Anthropic and OpenAI (24 lines total)
  - LOW-3 (carry-forward): maxTokens=4096 Anthropic default not in nexus.yaml.example — add max_tokens field under models.cloud, wire through ModelProviderConfig

- Per-Provider API Key Config (ProviderKeyConfig + ModelsConfig.GetApiKey): APPROVED (0 HIGH, 0 MEDIUM, 3 LOW)
  - LOW-1: Env var isolation in GetApiKey Tier 3 "returns null" tests — save/clear/restore env var in try/finally, or test the non-null Tier 3 case instead
  - LOW-2: Add YAML round-trip test for ProviderKeyConfig sections in ConfigLoaderTests — especially [YamlMember(Alias = "openai")] which would otherwise serialize as open_ai under UnderscoredNamingConvention
  - LOW-3: SettingsViewModel SaveSettings() must NOT write to _config.Models.Cloud.ApiKey after migration — remove that line; Cloud.ApiKey stays as read-only legacy field for Tier 2 fallback
  - Codebase fact: [YamlMember(Alias = "openai")] is required because UnderscoredNamingConvention would convert OpenAi -> open_ai; other providers (Gemini, Anthropic) round-trip correctly without an alias

## Recurring Pattern (new): config.Models.Cloud.ApiKey Sharing
- config.Models.Cloud is a single ModelProviderConfig; its ApiKey is semantically tied to the configured cloud provider.
- When registering multiple cloud providers in DI, always guard: use config.Models.Cloud.ApiKey only if config.Models.Cloud.Provider == "<target_provider>" (case-insensitive).
- Provider-specific env vars (ANTHROPIC_API_KEY, OPENAI_API_KEY, GEMINI_API_KEY) are safe to check without a provider guard — they are unambiguous.

- Sprint 2 Day 3 (InteractionSummarizer + Test Suite Gaps): NEEDS REVISION (2 HIGH, 2 MEDIUM, 3 LOW)
  - HIGH-1: ClearHistory() uses .GetAwaiter().GetResult() — sync-over-async, must become ClearHistoryAsync(). All callers (CLI, Desktop, E2EFlowTests) must be updated.
  - HIGH-2: GetRecentInteractionsAsync(5) hardcodes limit `5` — must be a named constant or MemoryConfig.RecentInteractionsFetchLimit = 5
  - MEDIUM-1: SemanticSearch.SearchInteractionsByEmbeddingAsync() required by FR-5/AC-5 has no design spec — architect must either add spec or defer AC-5 to Day 4
  - MEDIUM-2: Heuristic fallback "last assistant message" parsing underspecified; nested try/catch structure not described
  - LOW-1: ConfigureAwait(false) not mentioned for InteractionSummarizer library code — implementer reminder
  - LOW-2: _turnCount increment not thread-safe — use Interlocked.Increment or document concurrency assumption
  - LOW-3: New KnowledgeGraph methods use CancellationToken but existing methods don't — architect should state convention
  - Codebase confirmed: PromptBuilder.BuildInteractionSummaryPrompt() already exists (no Core changes needed for that)
  - Codebase confirmed: RoutingConfig.InteractionSummary already exists in NexusConfig — no new routing config needed
  - Codebase confirmed: E2EFlowTests line 250 constructs AgentService with 6 args (will break when 7th arg InteractionSummarizer is added)

### Recurring Pattern (new): void-to-async migration when sync-over-async appears
- If architecture spec says X().GetAwaiter().GetResult() with justification "never throws", flag HIGH.
- The never-throws guarantee removes exception risk but does NOT remove thread-blocking risk.
- Fix is always: change the public method to async Task, update all callers.
- This has appeared twice now (US-2.1 Day 1: ILlmProvider; Sprint 2 Day 3: ClearHistory).

- Sprint 2 Day 4 (McpClientManager rewrite + ToolRegistry + MCP SDK + integration tests): NEEDS REVISION (1 HIGH, 2 MEDIUM, 5 LOW)
  - HIGH-1: Circular dependency — architecture places MCP DI registration in Nexus.Core/ServiceCollectionExtensions.cs AND McpClientManager.ConnectAsync takes McpServerEntry (from Nexus.Core.Config) — this requires Core→Connectors AND Connectors→Core simultaneously. Fix: add McpServiceCollectionExtensions.cs in Nexus.Connectors; call AddNexusMcp() from CLI/Desktop entry points instead.
  - MEDIUM-1: InvokeToolAsync only catches McpException — incomplete "never throws" guarantee. Fix: catch Exception with structured log.
  - MEDIUM-2: Flow5 test (summarization → context) underspecified — no test sketch showing how to trigger summarization and assert context appearance.
  - LOW-1: ConfigureAwait(false) not mentioned for McpClientManager library code
  - LOW-2: IAsyncDisposable DisposeAsync() loop unspecified — verify if IMcpClient is IAsyncDisposable
  - LOW-3: _clients Dictionary not thread-safe — use ConcurrentDictionary
  - LOW-4: FakeLlmProvider ChatStreamAsync must use yield break (not throw/null)
  - LOW-5: nexus.yaml.example MCP section not shown in architecture doc
  - Codebase confirmed: Nexus.Core.csproj references only Nexus.Memory — no Connectors reference exists
  - Codebase confirmed: Nexus.Connectors.csproj has no ProjectReference to solution projects
  - Codebase confirmed: E2EFlowTests AgentService constructed with 7 args at line 251 — stable for Day 4
  - Codebase confirmed: McpServerConfig duplicate exists in both Nexus.Connectors AND Nexus.Core.Config — architecture correctly consolidates to McpServerEntry in Core.Config

- Sprint 2 Day 4 REVISED (IToolExecutor + McpToolExecutor + McpServiceCollectionExtensions): APPROVED (0 HIGH, 2 MEDIUM, 5 LOW)
  - All previous HIGH/MEDIUM/LOW issues from Round 1 addressed in revision
  - MEDIUM-1: IMcpClient mockability not confirmed — if IMcpClient is not an interface, unit tests need an adapter wrapper (IMcpClientWrapper)
  - MEDIUM-2: ConnectAsync call timing unspecified for Desktop — must be called async (background task or startup command in MainViewModel), not on UI thread
  - LOW-1: nexus.yaml.example MCP section still not shown — add stdio + HTTP/SSE example entries
  - LOW-2: DisposeAsync loop depends on IMcpClient being IAsyncDisposable — use pattern: `if (client is IAsyncDisposable ad) await ad.DisposeAsync(); else (client as IDisposable)?.Dispose();`
  - LOW-3: ConfigureAwait(false) is stated as rule but not per-method — implementer reminder for all awaits in McpClientManager and McpToolExecutor
  - LOW-4: E2EFlowTests line 251 AgentService constructor needs null for IToolExecutor? and ILogger? after refactor
  - LOW-5: IToolExecutor? parameter position must be before ILogger? in AgentService constructor signature
  - Codebase confirmed: Nexus.Connectors.csproj currently has NO project references — must add Core reference in implementation step 2
  - Codebase confirmed: AgentService constructor currently has 8 params (includes ILogger?=null) — IToolExecutor? will become param 8, logger becomes param 9

### Recurring Pattern (new): DI Registration Location for Cross-Layer Services
- When a service lives in Nexus.Connectors but references types from Nexus.Core.Config, DI registration CANNOT go in Nexus.Core/ServiceCollectionExtensions.cs.
- The correct pattern: put AddNexusMcp() (or equivalent) extension method inside Nexus.Connectors, call it from CLI/Desktop entry points after AddNexusAgent().
- This keeps the dependency direction: Core does NOT reference Connectors. Connectors CAN reference Core.
- Check this whenever new Connectors services need DI wiring.

- Sprint 2 Day 5 (ToolCallParser + AgentService tool loop + CLI single-query + ForceDirectedLayout): COMPLETE (no validation record — delivered in prior sprint without separate architecture doc review)

- Sprint 2 Day 6 (Hardening & Polish): NEEDS REVISION Round 1 (1 HIGH, 2 MEDIUM, 4 LOW)
  - HIGH-1: RegisterToolsFromServer has TOCTOU window — clear then add is not atomic on ConcurrentDictionary. Also: must use TryRemove(key, out _) not .Remove(key) (does not exist on ConcurrentDictionary).
  - MEDIUM-1: Static readonly SolidColorBrush/Pen is wrong Avalonia type — must use ImmutableSolidColorBrush and ImmutablePen (Avalonia.Media.Immutable) for thread safety in render pipeline.
  - MEDIUM-2: LayoutUpdated event is the sole animation trigger after removing OnPropertyChanged(nameof(Nodes)) — implementation must ensure MemoryGraphView.axaml.cs subscribes to the event; if missing, graph silently stops animating.
  - LOW-1: GeminiEmbeddingServiceTests uses MockHandler (different API than TestHttpMessageHandler) — 403 test must use local MockHandler, not new shared fake.
  - LOW-2: Extracting MockLlmClient requires deleting private nested class from InteractionSummarizerTests + EntityExtractorTests + adding using import; omitting deletion causes ambiguous name build error.
  - LOW-3: await Task.CompletedTask removal in RunMemoryCommandAsync is straightforward — existing awaits keep the method async.
  - LOW-4: Tools property returning _tools directly allows concurrent iteration during RegisterToolsFromServer — acceptable window, but add XML doc comment.
  - Codebase confirmed: MemoryGraphView.axaml.cs currently has empty constructor (no event subscriptions) — LayoutUpdated subscription must be added.
  - Codebase confirmed: ToolRegistry uses Dictionary (not ConcurrentDictionary yet) — confirmed by reading source.
  - Codebase confirmed: GraphCanvas.Render allocates new SolidColorBrush and new Pen per call — confirmed architecture's intent to cache.

### Recurring Pattern (new): Avalonia ImmutableBrush/ImmutablePen for Static Caching
- In Avalonia 11.x, SolidColorBrush and Pen are AvaloniaObject subclasses — NOT thread-safe for static readonly usage.
- For static/cached brush/pen objects in custom controls, use ImmutableSolidColorBrush and ImmutablePen from Avalonia.Media.Immutable.
- These are value-semantic, allocation-free at render time, and safe to share across threads.
- Node fill brushes that vary per node (by type or selection state) must remain per-call (cannot be static).
- Check any PR that introduces static readonly Pen/Brush in a custom Avalonia control.

### Recurring Pattern (new): ConcurrentDictionary Multi-Step Operations Are Not Atomic
- Single-key ConcurrentDictionary ops (_tools[k]=v, TryRemove) are safe individually.
- Multi-step patterns (remove-all-for-server then add-all-for-server) are NOT atomic — readers see a partial window.
- Fix options: (a) document the window as acceptable in an XML doc comment, (b) use ReaderWriterLockSlim if strict atomicity needed.
- Also: ConcurrentDictionary does NOT have .Remove(key) — must use .TryRemove(key, out _).
- Check any PR that iterates + bulk-modifies a ConcurrentDictionary in separate loops.

- US-3.2 (MemoryCompressor Archival): APPROVED (0 HIGH, 0 MEDIUM, 3 LOW)
  - Design: inline in conversation (no separate file)
  - LOW-1: ConfigureAwait(false) not mentioned — implementer reminder for all awaits in MemoryCompressor
  - LOW-2: No YAML round-trip tests for new MemoryConfig fields (ArchivePath, CompressionEnabled) — add to ConfigLoaderTests
  - LOW-3: Per-entity DeleteEntityAsync catch log message template not specified — suggest: "Failed to delete entity {EntityId} from DB after archival; entity remains in DB but is included in archive file"
  - Codebase confirmed: MemoryConfig.ArchiveThresholdDays already exists — no conflict
  - Codebase confirmed: IKnowledgeGraph already has GetEntitiesByLevelAsync, GetRelationsForEntityAsync, DeleteEntityAsync — no new interface methods required
  - Codebase confirmed: ConfigLoader.GetDatabasePath tilde-expansion pattern confirmed at line 44-52 — GetArchivePath follows identical pattern

- US-3.7 (Configurable Tool Call Limits & Minor Hardening): NEEDS REVISION (0 HIGH, 2 MEDIUM, 2 LOW)
  - File: .claude/agent-memory/architecture-validator/us-3-7-validation.md
  - MEDIUM-1: AggregateException.Flatten().InnerExceptions.First() must be .FirstOrDefault()?.Message ?? ex.Message — First() throws if InnerExceptions is empty
  - MEDIUM-2: Timeout test delay duration unspecified (must exceed 1s); assertion string "timed out after 1 seconds" must match actual message format exactly — recommend asserting via $"timed out after {config.Mcp.ToolCallTimeoutSeconds} seconds"
  - LOW-1: ConfigureAwait(false) not mentioned for the 10 new KnowledgeGraph CT implementations — implementer reminder
  - LOW-2: ShowHelp 'help' command entry confirmed missing from current codebase (line 530-556 Program.cs) — architecture fix is correct
  - Codebase confirmed: McpConfig only has Servers — MaxToolCallIterations and ToolCallTimeoutSeconds are net-new fields
  - Codebase confirmed: IKnowledgeGraph has 17 methods; 10 currently lack CancellationToken — no fake KG in tests, only real KnowledgeGraph with SQLite
  - Codebase confirmed: DisposeAsync currently iterates _clients directly without .ToArray() — .ToArray() fix is correct
  - Codebase confirmed: McpToolCallLoopTests.CreateAgent takes 4 params; overload with NexusConfig? config = null is additive (non-breaking)

- US-4.6 + US-4.4 (Desktop Tests + Empty State UI): APPROVED Round 2 (0 HIGH, 0 MEDIUM, 2 LOW carry-forward)
  - File: docs/validation/US-4.6-4.4-validation.md
  - All 3 HIGH and 1 MEDIUM issues from Round 1 confirmed fixed in revised architecture doc
  - LOW-1 carry-forward: LoadActionsAsync_SetsIsLoading test needs PropertyChanged collection pattern (List<bool>) — point-in-time assert will always see false after awaiting completed task
  - LOW-2 carry-forward: InternalsVisibleTo awareness for ConfigLoader.Save exception path in SettingsViewModelTests

- Sprint Phase 8.1 (Plan-then-Execute Hardening): APPROVED (0 HIGH, 0 MEDIUM, 5 LOW)
  - File: docs/validation/US-sprint-phase-8-1-hardening-validation.md
  - 0 AC fidelity mismatches — architect's self-check §13 caught and resolved the one deviation (F2c test name _Change → _Toggled) before submission
  - LOW-1: Requirements doc §FR-B2 says ex.Message but AC-B2 says exceptionTypeName — architecture correctly uses GetType().Name per AC; implementer must not follow requirements doc wording
  - LOW-2: EntityExtractor.ExtractAndPersistAsync virtual change touches Nexus.Memory (otherwise untouched this sprint) — implementation step 11
  - LOW-3: conversationText param in RunBackgroundExtraction becomes dead code — must add // AC-A2: unused comment per design
  - LOW-4: ConfigureAwait(false) reminder for B2 enumerator drain (MoveNextAsync, DisposeAsync)
  - LOW-5: _loggedDimMismatch/_loggedCacheCap non-atomic — benign (Risk R-5 accepted)
  - Codebase confirmed: SettingsSnapshot currently has 18 fields (ToolFilteringEnabled is last) — ToolPlanningEnabled becomes 19th
  - Codebase confirmed: ValidateToolFilteringEnabled takes (bool, string?) — new ValidateToolPlanningEnabled takes (bool, ModelProviderConfig) to check both Provider and Model

### Recurring Pattern (new): Test Fake Construction Chain Divergence
- When architecture specifies a helper method to construct a multi-dependency class (e.g., AgentService 11-param), always verify each intermediate constructor call against the actual source.
- MemoryContextBuilder: 2nd param is SemanticSearch, 3rd is IEmbeddingService? — easy to mistake the order.
- ModelRouter: takes RoutingConfig, accessed via config.Models.Routing (not config.Models).
- IInteractionSummarizer: SummarizeAsync returns Task<Interaction> — not SummarizeAndStoreAsync, not Task.
- Pattern: always grep public constructor signatures for all classes in the construction chain before approving test helper methods.

- US-4.1 (Markdown Rendering in Chat): APPROVED (0 HIGH, 2 MEDIUM, 3 LOW)
  - File: .claude/agent-memory/architecture-validator/us-4-1-validation.md
  - MEDIUM-1: MarkdownTextBlock owns DispatcherTimer with no cleanup — must override OnDetachedFromVisualTree() to stop timer + unregister Tick
  - MEDIUM-2: Process.Start for links must set UseShellExecute=true — silently fails on Linux otherwise
  - LOW-1: Markdig in test .csproj is redundant (transitive via Desktop) — harmless
  - LOW-2: SelectableTextBlock vs TextBlock for code blocks not specified
  - LOW-3: IsUser is init-only, so [NotifyPropertyChangedFor(nameof(IsAssistantNormal))] on IsError only is correct and sufficient

- WI-HRD-5 Sprint 4 Day 6 (Chat UX bubbles+scroll, CLI validation, Core+Memory reorganization): APPROVED (0 HIGH, 2 MEDIUM, 4 LOW)
  - File: .claude/agent-memory/architecture-validator/sprint-4-day-6-validation.md
  - MEDIUM-1: Namespace rename scope not fully specified — 48 files affected (25 test files `using Nexus.Core`, 23 `using Nexus.Memory`, plus src consumers); doc must state old flat namespaces are fully removed
  - MEDIUM-2: ChatView.axaml ScrollViewer has no x:Name — `FindControl<ScrollViewer>("MessagesScroller")` returns null silently; AXAML must add `x:Name="MessagesScroller"` to the existing ScrollViewer
  - LOW-1: OnLoaded DataContext subscription assumes DataContext set once before Loaded; no guard for DataContext changes post-load
  - LOW-2: MainWindowViewModel tests need FakeKnowledgeGraph dual-registered as both IKnowledgeGraph + IActionLogNotifier (same instance) in test ServiceCollection
  - LOW-3: OnLoaded/OnUnloaded base call order is correct (base first in Loaded, base last in Unloaded) — asymmetry is intentional Avalonia practice, no change needed
  - LOW-4: Rapid streaming scroll posts can queue multiple no-op Dispatcher.Posts; optional coalesce with _scrollPending flag

### Recurring Pattern (new): Unnamed AXAML Controls + FindControl
- If code-behind uses `FindControl<T>("Name")` to locate an AXAML element, the AXAML element MUST have `x:Name="Name"` — without it FindControl returns null silently.
- Always verify the AXAML file for matching x:Name when reviewing code-behind that calls FindControl.
- This is an easy miss when the AXAML was written before the code-behind was designed.

- US-Sprint-Phase-8 (ToolPlanner plan-then-execute): APPROVED Round 2 (0 HIGH, 0 MEDIUM, 0 LOW)
  - File: docs/validation/US-sprint-phase-8-tool-planning-validation.md
  - All 5 Round 1 issues resolved in revision (2 HIGH + 1 MEDIUM + 2 LOW all closed)
  - HIGH-1 fix: BuildPlanExecutionSystemPromptAsync is now exactly 2-param; modelName read internally from _config.Models.Local.Model
  - HIGH-2 fix: Retry message is now `$"You must call {step.MatchedToolName}. Use [TOOL_CALL: {{\"name\": \"{step.MatchedToolName}\", ...}}]"` — exact AC-5 template
  - MEDIUM-1 fix: §11 step 10 now documents FakeLlmProvider must use provider name "ollama" matching config.Models.Local.Provider default
  - LOW-1 fix: ConfigureAwait(false) now explicitly stated in §5 (ToolPlanner) and §6 (AgentService plan paths)
  - LOW-2 fix: Logger resolution note added to §8.1 DI registration
  - Codebase confirmed: DoomLoopTests:66-67 and McpToolCallLoopTests:70-71 need named-arg fix (still valid, unchanged from Round 1)

### Recurring Pattern (new): AC Self-Assessment ✅ Override — Always Re-verify
- Architecture docs often self-assess §15 "All rows match" but substitute richer requirements-doc text for the Sprint AC text.
- Always re-read the AC text vs architecture doc text character-by-character for: method signatures, retry message strings, prompt template text.
- §15 ✅ marks are not authoritative — the validator re-verifies each claim independently.

## Links to Detail Files
- patterns.md: recurring architecture patterns
