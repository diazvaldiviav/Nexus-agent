# Tester Agent Memory

## Project Facts
- Solution: NexusAgent.slnx, .NET 10, TFM net10.0
- Test projects: Nexus.Memory.Tests, Nexus.Core.Tests, Nexus.Integration.Tests
- Test framework: xUnit + hand-rolled fakes (no Moq/NSubstitute used in practice)
- Naming convention: MethodName_Scenario_ExpectedResult

## Known Pre-existing Failures
- DIFactoryTests.DI_OllamaProvider_ResolvesOllamaEmbeddingService — FAILS on hardware-intelligence branch (FallbackEmbeddingService returned instead of OllamaEmbeddingService). Confirmed pre-existing: fails even without this sprint's changes. Not caused by metadata hardening sprint.

## Test Counts by Sprint
- Sprint 1 Day 1 complete: 53 tests total (35 Memory + 13 Core + 5 Integration)
- Sprint 1 Day 2 complete: 66 tests total (48 Memory + 13 Core + 5 Integration)
  - Added: 17 new tests (EntityExtractorTests x17, MemoryContextBuilderTests x6)
  - FakeEmbeddingService added at: tests/Nexus.Memory.Tests/Fakes/FakeEmbeddingService.cs
- Sprint 1 Day 3 complete: 79 tests total (52 Memory + 14 Core + 13 Integration)
  - Added: 13 new tests (OpenAiEmbeddingServiceTests x4, E2EFlowTests x5, DIFactoryTests x3, ConfigLoaderTests x1)
  - New fakes in Integration.Tests: tests/Nexus.Integration.Tests/Fakes/FakeEmbeddingService.cs, MockLlmClient.cs
  - New source: src/Nexus.Memory/OpenAiEmbeddingService.cs
  - EmbeddingsConfig gained ApiKey field; ServiceCollectionExtensions gained openai provider branch
- Sprint 1 Day 4 complete: 85 tests total (52 Memory + 20 Core + 13 Integration)
  - Added: 6 new tests (OllamaLlmProviderTests x2, GeminiLlmProviderTests x4)
  - New source files: ILlmProvider.cs, OllamaLlmProvider.cs, GeminiLlmProvider.cs, LlmProviderFactory.cs, ConversationMessage.cs, AgentResponse.cs
  - AgentService refactored to use LlmProviderFactory with local/cloud fallback
  - ServiceCollectionExtensions gained ILlmProvider multi-registration + LlmProviderFactory singleton
  - TestHttpMessageHandler pattern used inline in both new test classes (captures LastRequest)
- Sprint 2 Day 1 complete: 91 tests total (52 Memory + 26 Core + 13 Integration)
  - Added: 6 new tests (AnthropicLlmProviderTests x3, OpenAiLlmProviderTests x3)
  - New source files: src/Nexus.Core/AnthropicLlmProvider.cs, src/Nexus.Core/OpenAiLlmProvider.cs
  - ServiceCollectionExtensions: Anthropic and OpenAI providers registered conditionally on API key presence
  - AgentService: token count logging via (length / 4) approximation in LogActionAsync calls (both ChatAsync and ChatStreamAsync)
  - TestHttpMessageHandler pattern reused inline (same shape as prior sprints: captures LastRequest)
- Sprint 2 Day 2 complete: 97 tests total (52 Memory + 32 Core + 13 Integration)
  - Added: 6 new tests (ModelsConfigGetApiKeyTests x6)
  - New source: ProviderKeyConfig class + 3 nullable sections (Gemini, Anthropic, OpenAi with YamlMember alias) in NexusConfig.cs
  - GetApiKey/GetEndpoint helpers with 3-tier fallback: dedicated section → Cloud match → env var
  - ServiceCollectionExtensions refactored to use config.Models.GetApiKey() throughout (DI blocks simplified)
  - ModelsConfigGetApiKeyTests uses no fakes — pure unit tests on config model methods, all synchronous
- Sprint 2 Day 4 complete: 118 tests total (71 Memory + 32 Core + 15 Integration)
  - Added: 2 new tests in Nexus.Integration.Tests (CloudFlowTests x2: Flow4, Flow5)
  - New source files: src/Nexus.Core/IToolExecutor.cs, src/Nexus.Connectors/McpToolExecutor.cs, src/Nexus.Connectors/McpServiceCollectionExtensions.cs
  - New fake: tests/Nexus.Integration.Tests/Fakes/FakeLlmProvider.cs
  - McpClientManager rewritten to use ModelContextProtocol SDK (McpClient.CreateAsync)
  - ToolRegistry.cs updated with RegisterToolsFromServer
  - Transport support: StdioClientTransport + HttpClientTransport (for SSE)
  - Tool invocation via McpClient.CallToolAsync → McpToolExecutor.InvokeToolAsync
  - Program.cs: `nexus memory stats` shows GetInteractionCountAsync result (AC-7 US-2.2)
  - Format violations in new files: none. Pre-existing violations unchanged.
- KnowledgeGraph new methods sprint: 141 tests total (76 Memory + 46 Core + 19 Integration)
  - Added: 3 new tests in KnowledgeGraphTests.cs
    - DeleteEntityAsync_RemovesEntityAndOrphanRelations (AC-6)
    - UpdateRelationEntityIdAsync_RePointsRelations (AC-7)
    - GetEntitiesByLevelAsync_FiltersCorrectly (AC-8)
  - New source methods: DeleteEntityAsync (transaction: delete relations + entity), UpdateRelationEntityIdAsync (transaction: UPDATE entity_id_1 + entity_id_2), GetEntitiesByLevelAsync (filter + ORDER BY relevance_score DESC)
  - Build: 0 errors, 0 warnings
- HRD sprint (housekeeping): 138 tests total (73 Memory + 46 Core + 19 Integration)
  - Added: 3 new tests
    - ForceDirectedLayoutTests: Step_EmptyNodeList_DoesNotThrow (HRD-3.3)
    - FallbackEmbeddingServiceTests: GenerateEmbeddingAsync_ForwardsCancellationToken_ToPrimaryService (HRD-3.4)
    - GeminiEmbeddingServiceTests: GenerateEmbeddingAsync_Forbidden_ThrowsInvalidOperationWithApiKeyMessage (HRD-3.5)
  - New shared fakes extracted: TestHttpMessageHandler (Core.Tests/Fakes/), MockLlmClient (Memory.Tests/Fakes/)
  - FakeEmbeddingService gained LastCancellationToken property
  - All LLM provider tests now use shared TestHttpMessageHandler (no more inline definitions)
  - ToolRegistry refactored to ConcurrentDictionary + TryRemove (HRD-2.1)
  - GraphCanvas: ImmutableSolidColorBrush/ImmutablePen cached as static readonly (HRD-1.4), nodeLookup cached (HRD-1.5), InvalidateVisual() called on layout tick via LayoutUpdated event (HRD-1.1)
  - MemoryGraphViewModel: dead OnPropertyChanged(nameof(Nodes)) removed (HRD-1.3), SelectAllFilters + ClearAllFilters relay commands added (HRD-2.6), LayoutUpdated event added
  - Program.cs: OrdinalIgnoreCase comparisons (HRD-2.3), AggregateException unwrap (HRD-2.4), dead Task.CompletedTask removed (HRD-2.2)
  - nexus.yaml.example: model names updated to qwen3:14b + gemini-2.5-flash-lite + claude-sonnet-4-6 (HRD-2.5)
  - Build: 0 errors, 0 warnings
- Sprint 2 Day 5 complete: 135 tests total (71 Memory + 45 Core + 19 Integration)
  - Added: 17 new tests (ToolCallParserTests x5, McpToolCallLoopTests x4, ForceDirectedLayoutTests x8)
  - New source files: src/Nexus.Core/ToolCallParser.cs, src/Nexus.Desktop/Layout/ForceDirectedLayout.cs
  - Modified: PromptBuilder.cs (tool defs injection), AgentService.cs (tool call loop in ChatAsync + ChatStreamAsync), Program.cs (single query mode, pipe support, exit codes)
  - New fake: tests/Nexus.Integration.Tests/Fakes/FakeToolExecutor.cs
  - ForceDirectedLayoutTests placed in Nexus.Core.Tests (not a separate Desktop.Tests project)
  - US-2.5 ACs verified by code inspection (single query, memory pipeline, entity extraction, streaming, exit codes, pipe)
  - US-2.6 AC-7 (animation) is UI-only — no automated test possible, code-verified via DispatcherTimer in MemoryGraphViewModel
  - Build: 0 errors, 0 warnings
- Sprint 2 Day 3 complete: 116 tests total (71 Memory + 32 Core + 13 Integration)
  - Added: 19 new tests in Nexus.Memory.Tests
    - InteractionSummarizerTests x8 (SummarizeAsync x4, CleanSummary x1, GenerateHeuristicSummary x2, LlmFailsAndPersistFails x1)
    - GeminiEmbeddingServiceTests x5 (dimensions, empty, unauthorized, rate-limit, constructor-missing-key)
    - FallbackEmbeddingServiceTests x5 (primary-succeeds, primary-fails, both-fail, no-fallback, null-primary)
    - Note: FallbackEmbeddingService had only 5 tests but last run shows 5 in results (including constructor guard)
  - New source: InteractionSummarizer.cs (IInteractionSummarizer + class), GeminiEmbeddingService.cs, FallbackEmbeddingService.cs
  - AgentService: IInteractionSummarizer injected; SummarizeAsync called on turn % SummarizationInterval and in ClearHistoryAsync
  - MemoryConfig gained SummarizationInterval (default 10) and RecentInteractionsFetchLimit (default 5)
  - MemoryContext gained RecentInteractions: List<Interaction>; MemoryContextBuilder.BuildContextAsync populates it
  - ServiceCollectionExtensions: IInteractionSummarizer registered as singleton before LlmProviderFactory
  - InteractionSummarizerTests uses inline MockLlmClient + FakeEmbeddingService; SQLite file-based with proper Dispose
  - GeminiEmbeddingService/FallbackEmbeddingService tests use inline MockHandler (captures LastRequestUri + LastRequestBody)

- Sprint 3 Day 5 complete: 170 tests total (101 Memory + 46 Core + 23 Integration)
  - Added: 7 new tests in MemoryCompressorTests.cs
    - CompressSummaries_GroupsWeeklyInteractions (AC-4: 3 interactions from same ISO week → 1 compressed)
    - CompressSummaries_GroupsMonthlyInteractions (AC-4: 5 interactions from same month >30d ago → 1 compressed)
    - CompressSummaries_SkipsRecentInteractions (AC-4: <7d old interactions not compressed)
    - CompressSummaries_SingleInGroup_NotCompressed (AC-4: group-of-1 not compressed)
    - CompressSummaries_NeverThrows_ReturnsZero (AC-4 resilience: ThrowingKnowledgeGraph → 0)
  - Added: 2 new tests in KnowledgeGraphTests.cs
    - DeleteInteraction_RemovesFromDb (new IKnowledgeGraph method)
    - GetInteractionsOlderThan_FiltersCorrectly (new IKnowledgeGraph method)
  - New IKnowledgeGraph methods: GetInteractionsOlderThanAsync, DeleteInteractionAsync
  - MemoryCompressor gained CompressSummariesAsync (weekly >7d, monthly >30d grouping)
  - AgentService: 11th param MemoryCompressor?, background ArchiveStaleEntitiesAsync when CompressionEnabled
  - RelevanceDecay: optional MemoryCompressor? hook in ApplyDecayAsync (best-effort, swallowed)
  - Program.cs: `nexus memory archive` + `nexus memory compress` CLI commands
  - ISO week boundary safety: test uses midWeek pinned to Wednesday with AddDays adjustment
  - Build: 0 errors, 0 warnings
  - Format violations: ZERO new violations; all pre-existing (KnowledgeGraphTests.cs inline object initializers)
- Sprint 3 Day 4 complete: 163 tests total (94 Memory + 46 Core + 23 Integration)
  - Added: 8 new tests in MemoryCompressorTests.cs
    - ArchiveStaleEntities_MovesOldArchiveEntitiesToJson (AC-1: stale Archive entity archived and deleted)
    - ArchiveStaleEntities_SkipsRecentArchiveEntities (AC-1: entity within threshold not archived)
    - ArchiveStaleEntities_NeverDeletesWorkingLevelEntities (AC-8: Working-level entities always preserved)
    - ArchiveStaleEntities_CreatesCorrectJsonFormat (AC-2: JSON structure, embedding Base64, relations included)
    - ArchiveStaleEntities_AppendsToExistingArchiveFile (AC-2: merges into existing archive-{date}.json, deduplication by Id)
    - ArchiveStaleEntities_GraphThrows_ReturnsZero (never-throws contract: outer exception swallowed, returns 0)
    - ArchiveStaleEntities_DeleteThrowsForOneEntity_StillCompletesArchive (partial delete failure: archive file still written, count returned)
    - ArchivePath_DefaultValue (AC-3: config default is "~/.nexus/archive/")
  - New source files: src/Nexus.Memory/Models/ArchiveModels.cs (ArchiveFile, ArchivedEntity, ArchivedRelation), src/Nexus.Memory/MemoryCompressor.cs
  - Modified: NexusConfig.cs (MemoryConfig gains ArchivePath + CompressionEnabled), ConfigLoader.cs (GetArchivePath helper), ServiceCollectionExtensions.cs (MemoryCompressor registered as singleton)
  - IKnowledgeGraph.cs: GetRelationsForEntityAsync signature gained CancellationToken parameter (already present, confirmed)
  - ThrowingKnowledgeGraph and DeleteFailingKnowledgeGraph stubs defined inline in test file (not in Fakes/)
  - MemoryCompressor.JsonOptions exposed as internal static (needed by test assertions for Deserialize)
  - Atomic write pattern: serialize to .tmp, File.Move with overwrite:true
  - Build: 0 errors, 0 warnings
  - Format violations: ZERO new violations in sprint files; all violations are pre-existing (KnowledgeGraphTests.cs)

- Sprint 3 Day 3 complete: 155 tests total (86 Memory + 46 Core + 23 Integration)
  - Added: 4 new tests in DeduplicationIntegrationTests.cs
    - AgentService_BackgroundDedup_CallsEntityResolver (AC-9: merge verified by entity count + mention sum)
    - AgentService_BackgroundDedup_NeverThrows_WhenResolverFails (AC-9 resilience: broken graph, dedup swallowed)
    - CLI_MemoryDedupe_FindsDuplicates (AC-7: FindDuplicatesAsync returns pairs above 0.85 threshold)
    - DeduplicationThreshold_DefaultValue_Is085 (config regression)
  - Modified: AgentService.cs (EntityResolver? field + 10-arg constructor, background dedup in ChatAsync + ChatStreamAsync)
  - Modified: ServiceCollectionExtensions.cs (EntityResolver registered before InteractionSummarizer; passed as 9th arg to AgentService factory)
  - Modified: Program.cs (`nexus memory dedupe` and `nexus memory dedupe --auto` handlers, help text updated)
  - New file: tests/Nexus.Integration.Tests/DeduplicationIntegrationTests.cs
  - IAsyncLifetime + IDisposable pattern; file-based SQLite with ClearAllPools() + GC.SuppressFinalize
  - FakeLlmProvider reused from existing Fakes folder
  - Build: 0 errors, 0 warnings
  - Format violations: ZERO new violations in sprint files; all pre-existing

- US-3.7 (hardening) complete: 184 tests total (101 Memory + 51 Core + 32 Integration)
  - Added: 2 new tests in McpToolCallLoopTests.cs
    - ChatAsync_ConfiguredMaxIterations_RespectsLimit (AC-1/AC-2/AC-7: MaxToolCallIterations=1, tool invoked exactly once)
    - ChatAsync_ConfiguredTimeout_UsesConfigValue (AC-1/AC-2/AC-7: ToolCallTimeoutSeconds=1, response contains "timed out after 1 seconds")
  - McpConfig gained MaxToolCallIterations (default 3) + ToolCallTimeoutSeconds (default 30) in NexusConfig.cs
  - AgentService: maxIterations from _config.Mcp.MaxToolCallIterations, timeout from _config.Mcp.ToolCallTimeoutSeconds (both ChatAsync + ChatStreamAsync)
  - IKnowledgeGraph: CancellationToken on all 19 public methods (was missing on ~10)
  - KnowledgeGraph: CancellationToken passed through to SQLite commands
  - McpClientManager.DisposeAsync: .ToArray() snapshot before iterating ConcurrentDictionary
  - Program.cs: AggregateException.Flatten() in single-query error path + chat error path, EscapeMarkup on all dynamic strings
  - ShowHelp: all commands documented (init, single-query, memory archive/compress/dedupe/dedupe --auto, connect, disconnect, servers, version, help)
  - nexus.yaml.example: mcp section documents max_tool_call_iterations + tool_call_timeout_seconds with comments
  - MemoryCompressorTests fake stubs (ThrowingKnowledgeGraph, DeleteFailingKnowledgeGraph): updated to include CancellationToken on all 19 IKnowledgeGraph methods
  - CreateAgent overload in McpToolCallLoopTests accepts optional NexusConfig? for configurable-limits tests
  - Build: 0 errors, 0 warnings
  - DelayToolExecutor defined inline in McpToolCallLoopTests (not in Fakes/) — one-off test helper

- US-3.7 hardening follow-up (deferred MEDIUM fixes) complete: 200 tests total (101 Memory + 51 Core + 48 Integration)
  - Added: 16 new tests (PromptBuilderTests x10 in Integration.Tests, ToolRegistryTests x6 in Integration.Tests)
  - Modified: McpToolCallLoopTests.cs (IDisposable removed → IAsyncLifetime only; DelayToolExecutor delay 5s→2s)
  - Modified: AgentService.cs (historySnapshot captured before Task.Run in both ChatAsync + ChatStreamAsync)
  - Modified: nexus.yaml.example (comment indentation fix in mcp section)
  - PromptBuilderTests: IAsyncLifetime, file-based SQLite with ClearAllPools()+GC.SuppressFinalize, real KnowledgeGraph+MemoryContextBuilder integration
  - ToolRegistryTests: pure in-memory, no DB, no fakes required — synchronous tests on ConcurrentDictionary
  - Build: 0 errors, 0 warnings

- MCP Persistence sprint complete: 175 tests total (101 Memory + 51 Core + 23 Integration)
  - Added: 5 new tests in McpPersistenceTests.cs (Nexus.Core.Tests)
    - Save_WithMcpServer_ThenLoad_RoundTrips (AC-1/AC-7: connect persists, load round-trips stdio entry with Args)
    - Save_DuplicateServerName_ReplacesExisting (AC-3: duplicate name replaces old entry; verify via single count + new-command)
    - Save_AfterRemovingServer_PersistsRemoval (AC-2: disconnect removes server; verify via empty Servers list)
    - Save_WithSseServer_RoundTripsUrl (AC-1/AC-7: SSE transport with Url field round-trips correctly)
    - Save_WithEnvDictionary_RoundTrips (AC-1: Env dictionary serializes and deserializes intact)
  - AC-4 (servers list command): verified by code inspection — RunServersCommand reads config.Mcp.Servers + GetServerStatus()
  - AC-5 (nexus.yaml.example): verified — filesystem (stdio+args+env) + git + SSE examples all present
  - AC-6 (help text): verified — connect/disconnect/servers all documented in ShowHelp()
  - Test pattern: IDisposable with temp directory per test (Guid suffix), GC.SuppressFinalize, best-effort cleanup
  - No SQLite — pure YAML file I/O, no ClearAllPools() needed
  - Build: 0 errors, 0 warnings

- Sprint 4 Day 1 complete: 220 tests total (101 Memory + 51 Core + 48 Integration + 20 Desktop)
  - NEW test project: tests/Nexus.Desktop.Tests/ (Avalonia.Headless.XUnit 11.2.5)
  - Added: 20 new tests across 4 ViewModel test classes
    - SettingsViewModelTests x4 (Constructor_LoadsConfigValues, SaveSettings_UpdatesConfigObject, Constructor_HandlesNullApiKeys, SaveSettings_ClearsEmptyApiKeys)
    - ActionLogViewModelTests x5 (LoadActionsAsync_PopulatesActions, LoadActionsAsync_FiltersByType, LoadActionsAsync_SetsIsLoading, FilterTypeChanged_TriggersReload, HasActions_FalseInitially_TrueAfterLoad)
    - MemoryGraphViewModelTests x5 (SelectNode_SetsSelectedNodeAndDetails, SelectNode_Null_ClearsDetails, LoadGraphAsync_PopulatesNodesAndEdges [AvaloniaFact], LoadGraphAsync_EmptyData_YieldsEmptyCollections [AvaloniaFact], HasNodes_AfterLoad_ReturnsTrue [AvaloniaFact])
    - ChatViewModelTests x6 (CanSend_ReturnsFalse_WhenInputTextEmpty, CanSend_ReturnsFalse_WhenIsProcessing, CanSend_ReturnsTrue_WhenInputTextSet, Messages_InitiallyEmpty, HasMessages_WhenEmpty_ReturnsFalse, HasMessages_AfterAdd_ReturnsTrue)
  - New fakes: FakeKnowledgeGraph (19 IKnowledgeGraph methods), Stubs.cs (StubEmbeddingService, StubLlmClient, StubLlmProvider, StubInteractionSummarizer)
  - TestAppBuilder: configures Avalonia headless mode for AvaloniaFact tests
  - HasMessages/HasNodes/HasActions computed properties verified in all 3 ViewModels (AC-7)
  - AXAML empty states verified by code inspection: ChatView (!HasMessages panel + 3 example prompt buttons), MemoryGraphView (!HasNodes panel), ActionLogView (!HasActions panel) (AC-8/9/10)
  - [AvaloniaFact] used for tests that call LoadGraphAsync (triggers DispatcherTimer) — other tests use plain [Fact]
  - Build: 0 errors, 0 warnings

- Error handling sprint (IAgentService + ErrorClassifier) complete: 240 tests total (103 Memory + 54 Core + 50 Integration + 33 Desktop)
  - Added: 9 new tests (ErrorClassifierTests x4 + ChatViewModelTests x5 new)
  - New source files: src/Nexus.Core/IAgentService.cs (ChatStreamAsync + ClearHistoryAsync + FlushPendingExtractionAsync), src/Nexus.Desktop/ViewModels/ErrorClassifier.cs (Classify returns Category+UserMessage+Detail)
  - New fake: tests/Nexus.Desktop.Tests/Fakes/FakeAgentService.cs (ExceptionToThrow, TokensToYield, ReceivedMessages)
  - ChatViewModel: IAgentService injection, HasError+ErrorMessage+ErrorDetail state, DismissErrorCommand, RetryCommand (_lastUserMessage replay), error on assistant message (IsError=true, ModelInfo="error")
  - AgentService now implements IAgentService; DI registers IAgentService pointing at AgentService singleton
  - SettingsViewModel: HasSuccess+HasError+StatusMessage fields; SaveSettings sets StatusMessage on success/failure
  - TestableChatViewModel inner class overrides DispatchToUI for synchronous test execution
  - Build: 0 errors, 0 warnings

- IActionLogNotifier sprint complete: 231 tests total (103 Memory + 54 Core + 50 Integration + 24 Desktop)
  - Added: 11 new tests across 4 files
    - ConfigLoaderCwdTests.Load_WhenLocalExists_PrefersLocalOverGlobal (AC-1/AC-2: local > global priority)
    - KnowledgeGraphNotifierTests x2: LogActionAsync_RaisesActionLoggedEvent, LogActionAsync_NoSubscribers_DoesNotThrow (AC-6/AC-7)
    - OnboardingWizardTests x3 new: GeneratedConfig_WithMcpServer_AddsServerEntry, GeneratedConfig_WithoutMcpServer_HasEmptyServers, GeneratedConfig_WithApiKeys_SetsProviderKeys (AC-3/AC-4/AC-5)
    - ActionLogViewModelTests x4 new + x5 updated: RealTimeAction_AppearsAtTop, RealTimeAction_FilteredOut_NotAdded, Dispose_UnsubscribesFromEvent, RealTimeAction_AllFilter_AlwaysAdded (AC-8/AC-9/AC-10/AC-11/AC-12)
  - New source files: src/Nexus.Memory/IActionLogNotifier.cs (ActionLogged event interface)
  - Modified: KnowledgeGraph.cs (implements IActionLogNotifier, raises event in LogActionAsync), ServiceCollectionExtensions.cs (dual IKnowledgeGraph+IActionLogNotifier registration), ActionLogViewModel.cs (IDisposable, event subscription, DispatchToUI virtual), FakeKnowledgeGraph.cs (IActionLogNotifier impl), OnboardingWizard.cs (MCP step 5/7 + overwrite protection + GenerateConfig 6th McpServerEntry? param)
  - TestableActionLogViewModel inner class overrides DispatchToUI for synchronous test execution
  - AC-3/AC-4 (OnboardingWizard step renumbering, overwrite protection): code-verified via OnboardingWizard.cs changes; GeneratedConfig tests cover param shape
  - Build: 0 errors, 0 warnings

- ConfigValidator + SettingsViewModel validation sprint complete: 268 tests total (103 Memory + 69 Core + 50 Integration + 46 Desktop)
  - Added: 28 new tests
    - ConfigValidatorTests x15 (Nexus.Core.Tests): ValidateDecayLambda x5 (in-range, below-min, above-max, boundary-min, boundary-max), ValidateLocalEndpoint x4 (http, https, empty, not-uri), ValidateSummarizationInterval x2 (valid, zero), ValidateRecentInteractionsFetchLimit x2 (in-range, above-max), CheckApiKeyWarning x1 (missing key), Validate_ValidConfig x1
    - SettingsViewModelValidationTests x13 (Nexus.Desktop.Tests): DecayLambda_OutOfRange_SetsError, DecayLambda_InRange_ClearsError, LocalEndpoint_Malformed_SetsError, LocalEndpoint_ValidUri_ClearsError, SummarizationInterval_Zero_SetsError, RecentInteractionsFetchLimit_AboveMax_SetsError, IsDirty_TrueAfterFieldChange, IsDirty_FalseAfterRevert, CanSave_FalseWhenNotDirty, CanSave_FalseWhenDirtyButInvalid, CanSave_TrueWhenDirtyAndValid, SaveSettings_ResetsIsDirty, ApiKeyWarning_ShownWhenProviderKeyMissing
  - New source file: src/Nexus.Core/Config/ConfigValidator.cs (public static, namespace Nexus.Core.Config)
  - Modified: src/Nexus.Desktop/ViewModels/SettingsViewModel.cs (IsDirty + SettingsSnapshot record, validation errors, CanSave guard, CheckDirty(), _isLoading guard, ApiKeyWarning)
  - Modified: src/Nexus.Desktop/Views/SettingsView.axaml ("(unsaved changes)" TextBlock bound to IsDirty, error TextBlocks per field, ApiKeyWarning border, NumericUpDown bounds)
  - ConfigValidator.Validate(NexusConfig) returns ValidationResult (record with Dictionary<string,string> Errors, IsValid, GetError)
  - AC-8: ConfigValidator in Nexus.Core — accessible from both Nexus.CLI (ProjectReference) and Nexus.Desktop
  - SaveSettings_ResetsIsDirty uses conditional assertion: HasSuccess → IsDirty=false; HasError (filesystem fail) → checks error state
  - Build: 0 errors, 0 warnings

- Hardening + file reorganization sprint complete: 299 tests total (103 Memory + 70 Core + 50 Integration + 76 Desktop)
  - Added: 17 new tests
    - ConfigValidatorTests x2 new (Validate_InvalidConfig_ReturnsSpecificErrors, CheckApiKeyWarning_ProviderMissingKey_ReturnsWarning — in addition to existing 15)
    - MarkdownTextBlockTests x9 NEW file (Render_WithMarkdown_ProducesControls, Render_SameTextTwice_ReturnsSameResult, Render_NullText_ReturnsEmpty, Text_SetProperty_StoresValue, Text_InitiallyNull_ContentPanelEmpty, Text_SetNull_ContentRemainsEmpty, Lifecycle_AttachAndTick_RendersContent, Lifecycle_SameTextTwice_RendersOnlyOnce, Lifecycle_DetachedGuard_PreventsRender)
    - ChatViewModelTests x3 new (SendAsync_NewMessage_ClearsExistingError, SendAsync_WhenServiceThrows_AssistantMessageHasIsError, ClearHistoryAsync_ClearsMessages_SetsStatusText)
    - MainWindowViewModelTests x4 NEW file (Constructor_DefaultsToChat, NavigateToMemoryGraph_SetsActiveTabAndView, NavigateToSettings_SetsActiveTabAndView, NavigateToActionLog_SetsActiveTabAndView_TriggersLoad)
  - All [AvaloniaFact] for MarkdownTextBlock lifecycle tests (headless Avalonia required for visual tree)
  - MainWindowViewModelTests uses real DI ServiceCollection with fakes — not Avalonia, plain [Fact]
  - NavigateToActionLog test verifies FakeKnowledgeGraph.GetRecentActionsCallCount increments (load triggered)
  - Lifecycle_DetachedGuard_PreventsRender: sets window.Content=null to detach, then ticks — asserts empty panel
  - Lifecycle_AttachAndTick_RendersContent: invokes OnDebounceTimerTick via reflection (BindingFlags.NonPublic|Instance)
  - Nexus.Core reorganized: Abstractions/, Providers/, Services/, Models/, Config/ — all namespaces match folder
  - Nexus.Memory reorganized: Abstractions/, Embedding/, Graph/, Processing/, Infrastructure/, Models/ — all namespaces match folder
  - ConfigValidator.Validate gate added to Program.cs Phase 3b — before DI setup, returns exit code 1 on error
  - Build: 0 errors, 0 warnings

- HW WmiCpuProfiler sprint complete: 386 tests total (103 Memory + 70 Core + 50 Integration + 76 Desktop + 86 Hardware + 1 Models)
  - Added: 18 new tests in Nexus.Hardware.Tests
    - WmiCpuProfilerTests x11 (ProfileAsync x5, MapArchitecture_KnownValues x4 [Theory], MapArchitecture_UnknownValue, ComputeSimdScore, Constructor_NullWmiQuery, ProfileAsync_ScoresAreCapped, ProfileAsync_ComException)
    - SensorSnapshotTests x2 (Constructor_SetsAllProperties, Equality_SameValues_AreEqual)
    - SystemHealthSnapshotTests x2 (Constructor_SetsAllProperties, Equality_SameValues_AreEqual)
  - New source files: src/Nexus.Hardware.Windows/Profilers/WmiCpuProfiler.cs, src/Nexus.Hardware.Windows/Internals/{IWmiQuery.cs, WmiQueryService.cs, IDxgiAdapterProvider.cs, DxgiAdapterInfo.cs, MemoryStatusResult.cs}
  - New Monitoring types: src/Nexus.Hardware/Monitoring/SensorSnapshot.cs (6-field record), src/Nexus.Hardware/Monitoring/SystemHealthSnapshot.cs (4-field record)
  - ISensorMonitor evolved: ReadAsync(ct) + IsAvailable (was empty marker interface)
  - Nexus.Hardware.Windows.csproj: 6 NuGet packages (LibreHardwareMonitorLib, DI.Abstractions, Logging.Abstractions, PerformanceCounter, System.Management, Vortice.DXGI)
  - Nexus.Hardware.Tests.csproj: NSubstitute 5.3.0 added
  - Placeholder.cs deleted (confirmed absent)
  - FakeWmiQuery: hand-rolled fake (params Dictionary[], ThrowingWmiQuery inner class via Throwing(ex) factory)
  - WmiCpuProfiler: internal sealed, IWmiQuery + ILogger constructor, Task.Run for WMI, ManagementException+COMException → degraded envelope, MapArchitecture + ResolveArchitectureClass + ComputeSimdScore all internal static (testable via InternalsVisibleTo)
  - Build: 0 errors, 0 warnings

- Markdown rendering sprint complete: 282 tests total (103 Memory + 69 Core + 50 Integration + 60 Desktop)
  - Added: 14 new test runs in MarkdownRendererTests.cs (13 methods + 1 AvaloniaTheory with 2 InlineData cases)
    - Render_BoldText_ReturnsBoldRun (AC-1)
    - Render_ItalicText_ReturnsItalicRun (AC-1)
    - Render_H1_ReturnsFontSize24 (AC-2)
    - Render_H2_ReturnsFontSize20 (AC-2)
    - Render_H3_ReturnsFontSize16 (AC-2)
    - Render_InlineCode_ReturnsMonospaceRun (AC-3)
    - Render_FencedCodeBlock_ReturnsBorderWithBackground (AC-4)
    - Render_BulletList_ReturnsIndentedItems (AC-5)
    - Render_NumberedList_ReturnsNumberedItems (AC-5)
    - Render_Link_ReturnsStyledElement (AC-6)
    - Render_PlainText_ReturnsSingleTextBlock (AC-7)
    - Render_NullOrEmpty_ReturnsEmptyList x2 (null + empty edge cases)
    - Render_InlineLink_WithSurroundingText_ReturnsRunWithLinkColor (inline link styling)
  - New source files: src/Nexus.Desktop/Controls/MarkdownRenderer.cs (static, never throws, uses Markdig), src/Nexus.Desktop/Controls/MarkdownTextBlock.cs (UserControl, DispatcherTimer debounce 250ms)
  - ChatViewModel.ChatMessage: added IsAssistantNormal computed property (!IsUser && !IsError), [NotifyPropertyChangedFor(nameof(IsAssistantNormal))] on _isError field
  - ChatView.axaml: plain TextBlock (IsVisible=!IsAssistantNormal) + MarkdownTextBlock (IsVisible=IsAssistantNormal) side-by-side per message
  - AC-8 (debounce 250ms): verified by code inspection only — DispatcherTimer.Interval=250ms, Stop/Start on TextProperty changed, OnAttachedToVisualTree subscribes, OnDetachedFromVisualTree unsubscribes + _isDetached guard
  - AC-9 (self-contained control): verified by code inspection — MarkdownTextBlock is UserControl in its own file, no external dependencies beyond Markdig
  - All tests use [AvaloniaFact]/[AvaloniaTheory] (headless Avalonia required for TextBlock/Run Inlines to resolve)
  - Markdig added as NuGet dependency to Nexus.Desktop project
  - Build: 0 errors, 0 warnings

- Hardware Envelopes sprint complete: 338 tests total (103 Memory + 70 Core + 50 Integration + 76 Desktop + 38 Hardware + 1 Models)
  - Added: new Nexus.Hardware.Tests project with 38 tests
    - CpuEnvelopeTests x7, RamEnvelopeTests x7, GpuEnvelopeTests x9, HostCapabilityProfileTests x7, EnumTests x8
  - New source files: CpuEnvelope.cs, RamEnvelope.cs, GpuEnvelope.cs (all namespace Nexus.Hardware.Envelopes), HostCapabilityProfile.cs
  - Nexus.Hardware.csproj has ZERO PackageReference entries (pure BCL only)
  - Build: 0 errors, 0 warnings

- HostStateClassifier sprint complete: 362 tests total (103 Memory + 70 Core + 50 Integration + 76 Desktop + 62 Hardware + 1 Models)
  - Added: 24 new tests in HostStateClassifierTests.cs (Hardware.Tests)
    - ClassifyCpu_ReturnsExpectedState x8 [Theory/InlineData]: boundary cases at 0.0, 0.24, 0.25, 0.49, 0.50, 0.74, 0.75, 1.0
    - ClassifyRam_ReturnsExpectedState x8 [Theory/InlineData]: boundary cases at 0, 3.999B, 4B, 7.999B, 8B, 15.999B, 16B, 32B
    - ClassifyGpu_ReturnsExpectedState x8 [Theory/InlineData]: boundary cases at -1, 0, 1, 3.999B, 4B, 7.999B, 8B, 16B
  - New source: src/Nexus.Hardware/Abstractions/ISensorMonitor.cs (empty marker interface)
  - New source: src/Nexus.Hardware/HostStateClassifier.cs (public static class, 8 internal const thresholds)
  - ISensorMonitor: file-scoped namespace Nexus.Hardware.Abstractions, XML doc comment, single-line interface body
  - HostStateClassifier: 3 CPU thresholds (0.25/0.50/0.75), 3 RAM thresholds (4B/8B/16B), 2 GPU thresholds (4B/8B)
  - GPU None boundary: <= 0 (inclusive, handles negative safeGpuBudget from NoGpu factory)
  - Nexus.Hardware.csproj: InternalsVisibleTo for Hardware.Tests, ZERO PackageReference elements
  - Build: 0 errors, 0 warnings

## Known Formatting Issues
- dotnet format --verify-no-changes exits with code 2 (pre-existing formatting violations)
- Violations are CRLF/indentation whitespace in: KnowledgeGraph.cs, SemanticSearch.cs,
  RelevanceDecay.cs, Program.cs, NexusConfig.cs, DatabaseInitializer.cs, MemoryContextBuilder.cs, etc.
- Sprint 2 Day 4 new files (IToolExecutor.cs, McpToolExecutor.cs, McpServiceCollectionExtensions.cs,
  CloudFlowTests.cs, FakeLlmProvider.cs) have ZERO new format violations
- These are pre-existing debt — not introduced by the sprint under test
- Do NOT fail a sprint for pre-existing format violations; note them as known debt

## SQLite / Windows Disposal Pattern
- Always call SqliteConnection.ClearAllPools() before File.Delete() in Dispose()
- Always include GC.SuppressFinalize(this) in Dispose()
- EntityExtractorTests and MemoryContextBuilderTests both use file-based SQLite (temp path + Guid)

## Fake/Mock Patterns Used
- FakeEmbeddingService: tracks CallCount, CalledWithTexts[], and LastCancellationToken; configurable fixed embedding or exception
- MockLlmClient (shared at tests/Nexus.Memory.Tests/Fakes/MockLlmClient.cs): tracks LastPrompt, delegates to Func<string, Task<string>>
- TestHttpMessageHandler (shared at tests/Nexus.Core.Tests/Fakes/TestHttpMessageHandler.cs): captures LastRequest, returns configurable content + status code. Used by all LLM provider tests.
- Integration tests use real DI container (ServiceCollection + AddNexusAgent) with in-memory SQLite db path

## AC-11 / AC-12 Status (Sprint 1 Day 3)
- AC-11 covered by E2EFlowTests.BugFix001_AgentMaintainsConversationHistory — validates contract only
  (ConversationHistory starts empty, is IReadOnlyList). Full ChatAsync accumulation still needs Ollama.
- AC-12 covered by E2EFlowTests.BugFix002_ExtractionPromptRequiresEnglishOutput — asserts prompt
  contains "ALL output MUST be in English". PASS.
- Treat AC-11 as PARTIAL (automated contract check passes; full conversation accumulation manual)

## HW Sprint 2 Day 2 complete: 401 tests total (101 Memory + 70 Core + 50 Integration + 76 Desktop + 103 Hardware + 1 Models)
  - Added: 15 new Win32RamProfilerTests (4 [Fact] + 1 [Theory] x10 inline data = 14 theory cases + 1 constructor = 15)
    - ProfileAsync_32GB_20GBAvail_CorrectBudgets (AC-5/6/7)
    - ProfileAsync_8GB_1GBAvail_HighPressure (AC-8)
    - ProfileAsync_ZeroAvailable_NotViable (AC-5/6/7)
    - ProfileAsync_ExceptionThrown_DegradedEnvelope (AC-9)
    - ClassifyPressure_BoundaryValues x10 inline data (AC-8)
    - Constructor_NoException (AC-1)
  - New source: Win32RamProfiler.cs (internal partial class, LibraryImport P/Invoke, MEMORYSTATUSEX 64-byte struct), DxgiAdapterProvider.cs, DxgiGpuProfiler.cs
  - New fake: TestableRamProfiler.cs (overrides GetMemoryStatus(), Throwing factory) (AC-11)
  - AllowUnsafeBlocks: true in Nexus.Hardware.Windows.csproj (AC-10)
  - No GPU test files exist (US-2.3 tests deferred to Day 3 per spec)
  - Build: 0 errors, 0 warnings
