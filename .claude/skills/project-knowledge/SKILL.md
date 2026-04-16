# Skill: Project Knowledge — Nexus Agent (.NET 10)

> Load this skill when working on any project task. Contains the architecture overview, file structure, technology stack, conventions, and design decisions for the Nexus Agent application.

---

## Source of Truth

All architectural decisions, flows, and design specifications are defined in these two documents:

- **`docs/nexus-agent-documento-completo.md`** — Complete technical document: vision, architecture (4 layers), technology stack, memory system design, model router, MCP connectivity, desktop UI scope, configuration, and all confirmed technical decisions.
- **`docs/architecture-diagram.md`** — Mermaid diagrams for all logical flows: ChatAsync (main loop), Startup, Semantic Search, Entity Extraction, MemoryContextBuilder, Function Calling + MCP, Decay Temporal, Graph Visualization, Settings, and how they connect.

**When in doubt, these documents are authoritative.** Sprint plans, requirements, and architecture designs must align with them — not the other way around.

---

## What is Nexus Agent?

A **personal AI agent** built in C# (.NET 10) with:
- **Persistent knowledge graph memory** — remembers everything across conversations
- **LLM orchestration** — local (Ollama) and cloud (Anthropic/OpenAI/Google) providers
- **MCP connectivity** — connects to external tools via Model Context Protocol
- **Desktop UI** — Avalonia cross-platform app with chat, graph visualization, settings
- **CLI** — Spectre.Console terminal interface

**Core philosophy:** "La memoria ES el producto." All design decisions prioritize the knowledge graph.

---

## Technology Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10, C# 13 |
| Orchestration | AgentService + ModelRouter + PromptBuilder + MemoryContextBuilder |
| LLM Local | Ollama (qwen3:14b) via HTTP API |
| LLM Cloud | Anthropic / OpenAI / Google via HTTP API |
| Embeddings Local | Ollama + nomic-embed-text (768d) |
| Embeddings Cloud | OpenAI text-embedding-3-small |
| Database | SQLite (Microsoft.Data.Sqlite) |
| Vector Search | Cosine similarity in-process |
| MCP Client | ModelContextProtocol NuGet SDK |
| Desktop UI | Avalonia UI 11.x (MVVM, CommunityToolkit.Mvvm) |
| CLI | Spectre.Console |
| Config | YAML (YamlDotNet) |
| DI | Microsoft.Extensions.DependencyInjection |
| Testing | xUnit + Moq / NSubstitute |

---

## Architecture — 4 Layers

```
CAPA 0: INTERFAZ        → Desktop (Avalonia) + CLI (Spectre.Console)
CAPA 1: ORQUESTACION    → AgentService + ModelRouter + PromptBuilder
CAPA 2: MOTOR DE MEMORIA → KnowledgeGraph + SemanticSearch + EntityExtractor + RelevanceDecay
CAPA 3: CONECTIVIDAD    → McpClientManager + ToolRegistry
```

**Dependency flow:** Interface → Core → Memory. Connectors → Core (one-way). **No circular references.**

---

## Project Structure

```
nexus-agent/
├── src/
│   ├── Nexus.Memory/             # Knowledge graph, embeddings, semantic search, decay
│   │   ├── Abstractions/         # Interfaces (Nexus.Memory.Abstractions)
│   │   │   ├── IKnowledgeGraph.cs   # Knowledge graph data access (19 methods, all with CancellationToken)
│   │   │   ├── IEmbeddingService.cs  # Text embedding generation
│   │   │   ├── ILlmClient.cs        # LLM text generation (cross-layer)
│   │   │   └── IActionLogNotifier.cs # Real-time action log events
│   │   ├── Embedding/            # Embedding services (Nexus.Memory.Embedding)
│   │   │   ├── EmbeddingOptions.cs   # Config record (Endpoint, Model, Dimensions)
│   │   │   ├── OllamaEmbeddingService.cs
│   │   │   ├── OpenAiEmbeddingService.cs
│   │   │   ├── GeminiEmbeddingService.cs
│   │   │   └── FallbackEmbeddingService.cs
│   │   ├── Graph/                # Knowledge graph + search (Nexus.Memory.Graph)
│   │   │   ├── KnowledgeGraph.cs     # SQLite CRUD (implements IKnowledgeGraph + IActionLogNotifier)
│   │   │   ├── SemanticSearch.cs     # Cosine similarity search
│   │   │   ├── EntityExtractor.cs    # 3-level fallback extraction + auto-embedding
│   │   │   └── EntityResolver.cs     # Duplicate detection + merge
│   │   ├── Processing/           # Memory pipeline (Nexus.Memory.Processing)
│   │   │   ├── InteractionSummarizer.cs  # LLM + heuristic fallback (IInteractionSummarizer)
│   │   │   ├── MemoryContextBuilder.cs   # 3-level memory context with semantic search
│   │   │   ├── MemoryCompressor.cs       # Archive + compress old interactions
│   │   │   └── RelevanceDecay.cs         # Time-based decay + archival hook
│   │   ├── Infrastructure/       # Database (Nexus.Memory.Infrastructure)
│   │   │   └── DatabaseInitializer.cs
│   │   └── Models/               # Entity, Relation, Interaction, DuplicatePair, ArchiveModels POCOs
│   │
│   ├── Nexus.Core/              # Agent orchestration, model routing, prompts
│   │   ├── Abstractions/        # Interfaces (Nexus.Core.Abstractions)
│   │   │   ├── IAgentService.cs    # ChatStreamAsync, ClearHistoryAsync, FlushPendingExtractionAsync
│   │   │   ├── ILlmProvider.cs     # ChatAsync, ChatStreamAsync
│   │   │   ├── IToolExecutor.cs    # Cross-layer (impl in Connectors). GetToolDefinitionsForPrompt() + GetToolDefinitionsForPrompt(string? modelName) default interface method for model-aware tool filtering
│   │   │   └── ISchemaValidator.cs  # Schema validation contract + SchemaValidationResult (impl in Connectors)
│   │   ├── Providers/           # LLM providers (Nexus.Core.Providers)
│   │   │   ├── OllamaLlmProvider.cs
│   │   │   ├── GeminiLlmProvider.cs
│   │   │   ├── AnthropicLlmProvider.cs
│   │   │   ├── OpenAiLlmProvider.cs
│   │   │   ├── OllamaLlmClient.cs  # ILlmClient impl via Ollama HTTP
│   │   │   └── LlmProviderFactory.cs
│   │   ├── Services/            # Orchestration (Nexus.Core.Services)
│   │   │   ├── AgentService.cs      # Main agent loop + output truncation + doom loop detection
│   │   │   ├── ContextWindowManager.cs # Context window estimation + conversation compaction
│   │   │   ├── ModelRouter.cs       # Local vs cloud selection
│   │   │   ├── OutputTruncator.cs   # Static: head/tail line truncation + UTF-8 safe byte truncation (TruncatedOutput record)
│   │   │   ├── PromptBuilder.cs     # Memory context + tool definitions. BuildSystemPromptAsync(userQuery, modelName?, ct) passes modelName to IToolExecutor for model-aware tool filtering
│   │   │   └── ToolCallParser.cs    # Multi-format tool call parser: [TOOL_CALL:] marker + <tool_call> XML + raw JSON fallback, markdown fence stripping, brace-walking state machine (WalkJsonObject 3-tuple with endedInString), mid-string JSON repair (closes unclosed quotes before appending braces), IsParsableJson guard, TryParseAll multi-tool extraction with ParsedToolCall position tracking + IsOverlapping dedup, TryParseJson shared helper
│   │   ├── Models/              # POCOs (Nexus.Core.Models)
│   │   │   ├── AgentResponse.cs
│   │   │   └── ConversationMessage.cs
│   │   ├── Config/
│   │   │   ├── ConfigLoader.cs
│   │   │   ├── NexusConfig.cs
│   │   │   └── ConfigValidator.cs    # Static validation: Memory + Models + MCP config (scalar ranges, McpServerEntry transport/url)
│   │   └── ServiceCollectionExtensions.cs # DI registration (stays at root)
│   │
│   ├── Nexus.Connectors/        # External tool connectivity (MCP SDK)
│   │   ├── McpClientManager.cs  # MCP client: stdio/SSE transport, tool discovery, invocation
│   │   ├── ToolRegistry.cs      # Dynamic tool registry (ConcurrentDictionary, thread-safe) + ToolResolution record + ResolveTool() fuzzy name resolution (exact → case-insensitive → Levenshtein ≤2 → fail)
│   │   ├── McpToolExecutor.cs   # IToolExecutor impl: depends on IMcpClientManager (not concrete), routes tool calls through MCP, uses ResolveTool() for fuzzy name matching. GetToolDefinitionsForPrompt(string? modelName) override: when ToolFilteringEnabled + modelName non-empty → delegates to ToolPromptFormatter.Format(); otherwise falls back to unfiltered ToolRegistry output
│   │   ├── SchemaValidator.cs   # ISchemaValidator impl: validates tool args against InputSchema (required check, type coercion string→bool/number/array, unknown arg stripping)
│   │   ├── McpServiceCollectionExtensions.cs # AddNexusMcp() DI extension
│   │   └── ToolFiltering/       # Tool complexity classification for small-model filtering
│   │       ├── ToolComplexityTier.cs        # Enum: Simple, Moderate, Complex
│   │       ├── ToolCallingTier.cs           # Enum: Limited, Capable, Full (model capability tier)
│   │       ├── ToolComplexityScore.cs       # Record: 7-field scoring result (ToolName, Score, Tier, RequiredParamCount, TotalParamCount, MaxNestingDepth, HasArrayOfObjects)
│   │       ├── IToolComplexityClassifier.cs # Interface: Classify(ToolDefinition) → ToolComplexityScore
│   │       ├── ToolComplexityClassifier.cs  # Sealed classifier: weighted score formula (0.15*req+0.08*total+0.25*depth+0.35*arrayOfObj+0.05*enum+0.15*semantic+0.05*optExcess), named constants (SimpleTierThreshold=0.50, ModerateTierThreshold=0.80, MaxNestingDepthCap=5), null-safe Description/Name access, debug logging after score computation
│   │       ├── ToolCapabilityResolver.cs   # Static: Resolve(string? modelName) → ToolCallingTier via regex param-count extraction, named constants (LimitedModelThreshold=3.0, CapableModelThreshold=8.0), safe default Full
│   │       └── ToolPromptFormatter.cs    # Sealed: Format(tools, modelName) → filtered prompt string. ILogger support. Delegates tool rendering to ToolRegistry.RenderToolToStringBuilder(). Combines ToolComplexityClassifier + ToolCapabilityResolver to partition tools into included (with optional hints) and excluded (with 3-tier BuildExclusionHint: WorkflowOverrides → same-server Simple → fallback). Full-tier parity with ToolRegistry.GetToolDefinitionsForPrompt()
│   │
│   ├── Nexus.Desktop/           # Avalonia UI (MVVM)
│   │   ├── Views/               # AXAML views
│   │   │   ├── ChatView.axaml
│   │   │   ├── MemoryGraphView.axaml
│   │   │   ├── SettingsView.axaml
│   │   │   └── ActionLogView.axaml
│   │   ├── ViewModels/          # MVVM ViewModels
│   │   │   ├── ChatViewModel.cs  # Chat MVVM: ChatMessage (ObservableObject, IsAssistantNormal computed) + streaming, HasMessages, SetExamplePromptCommand, error handling (HasError/ErrorMessage/ErrorDetail), RetryCommand, DismissErrorCommand, DispatchToUI virtual
│   │   │   ├── ErrorClassifier.cs  # Static error classifier: HttpRequestException→connection, TaskCanceledException→timeout, unauthorized→apikey, default→generic
│   │   │   ├── MemoryGraphViewModel.cs  # Graph VM: HasNodes computed property
│   │   │   ├── SettingsViewModel.cs  # Settings MVVM: ConfigValidator integration, IsDirty/SettingsSnapshot dirty tracking (18-field record), CanSave guard, inline validation errors (Memory + MCP fields), ApiKeyWarning, HasError/HasSuccess banners, MCP tool settings (MaxToolCallIterations, ToolCallTimeoutSeconds, MaxOutputLines, MaxOutputBytes, SchemaValidationEnabled, ToolFilteringEnabled) with reactive OnChanged validation
│   │   │   └── ActionLogViewModel.cs  # Action log VM: HasActions computed property, DispatchToUI virtual
│   │   ├── Layout/
│   │   │   └── ForceDirectedLayout.cs  # Fruchterman-Reingold force-directed graph layout
│   │   └── Controls/
│   │       ├── GraphCanvas.cs   # Custom graph rendering control with cached ImmutableBrush/Pen, nodeLookup cache
│   │       ├── MarkdownRenderer.cs  # Static helper: markdown string → IReadOnlyList<Control> via Markdig AST (Catppuccin Mocha palette, DisableHtml security)
│   │       └── MarkdownTextBlock.cs  # UserControl: StyledProperty<string?> Text, 250ms DispatcherTimer debounce, attach/detach lifecycle
│   │
│   ├── Nexus.CLI/               # Terminal interface
│   │   ├── OnboardingWizard.cs  # First-use setup wizard: 7-step (Ollama, chat model, embed model, API keys, MCP filesystem, config gen, save with overwrite protection)
│   │   └── Program.cs           # Spectre.Console chat loop + memory/connect/disconnect/servers/init commands
│   │
│   ├── Nexus.Hardware/          # Hardware Intelligence — pure contracts (ZERO NuGet deps)
│   │   ├── Abstractions/        # Profiler interfaces (Nexus.Hardware.Abstractions)
│   │   │   ├── ICpuProfiler.cs      # Task<CpuEnvelope> ProfileAsync(ct)
│   │   │   ├── IRamProfiler.cs      # Task<RamEnvelope> ProfileAsync(ct)
│   │   │   ├── IGpuProfiler.cs      # Task<GpuEnvelope> ProfileAsync(ct)
│   │   │   ├── IHostProfiler.cs     # Task<HostCapabilityProfile> BuildProfileAsync(ct)
│   │   │   └── ISensorMonitor.cs    # Task<SensorSnapshot> ReadAsync(ct) + bool IsAvailable
│   │   ├── States/              # Discrete state enums (Nexus.Hardware.States)
│   │   │   ├── CpuState.cs          # Weak, Moderate, Strong, HighEnd
│   │   │   ├── RamState.cs          # Tight, Adequate, Comfortable, Abundant
│   │   │   ├── GpuState.cs          # None, Limited, Capable, Strong
│   │   │   ├── ArchitectureState.cs # NativeOptimal, NativeCompatible, EmulatedPenalty, Unsupported
│   │   │   ├── FeasibilityResult.cs # Rejected, FeasibleWithCaution, Feasible, Optimal
│   │   │   ├── PlacementStrategy.cs # CpuOnly, GpuFull, GpuPartial, HybridFallback
│   │   │   ├── SafetyLevel.cs       # Unsafe, Caution, Safe, Comfortable
│   │   │   └── PressureLevel.cs     # None, Low, Medium, High, Critical
│   │   ├── Envelopes/           # Hardware measurement snapshots (Nexus.Hardware.Envelopes)
│   │   │   ├── CpuEnvelope.cs       # 5-param record + IsViable() → CpuInferenceScore > 0
│   │   │   ├── RamEnvelope.cs       # 4-param record + IsViable() → SafeModelRamBudget > 0
│   │   │   └── GpuEnvelope.cs       # 6-param record + IsViable() → true, NoGpu() factory
│   │   ├── Monitoring/           # Runtime sensor/health data (Nexus.Hardware.Monitoring)
│   │   │   ├── SensorSnapshot.cs    # 6-field record (CpuTemp, GpuTemp, CpuClock, CpuLoad, GpuLoad, ReadAt)
│   │   │   └── SystemHealthSnapshot.cs # 4-field record (CpuUsage, AvailableRam, PagesPerSec, ReadAt)
│   │   ├── HostCapabilityProfile.cs # 11-param aggregate record (envelopes + states + Architecture + DateTime)
│   │   └── HostStateClassifier.cs   # Static classifier: ClassifyCpu/Ram/Gpu → state enums via thresholds
│   │
│   ├── Nexus.Hardware.Windows/  # Windows-specific hardware detection (WMI, DXGI, P/Invoke)
│   │   ├── HardwareServiceCollectionExtensions.cs # Public static: AddNexusHardwareWindows() — registers 10 services (4 infra + 3 profilers + 1 composite + 2 monitors)
│   │   ├── Internals/           # Internal abstractions (Nexus.Hardware.Windows.Internals)
│   │   │   ├── IWmiQuery.cs         # Internal interface: Query(string wql) → IReadOnlyList<IReadOnlyDictionary>
│   │   │   ├── WmiQueryService.cs   # Internal sealed: ManagementObjectSearcher with COM disposal
│   │   │   ├── IDxgiAdapterProvider.cs # Internal interface: GetAdapters() → IReadOnlyList<DxgiAdapterInfo>
│   │   │   ├── DxgiAdapterProvider.cs # Internal sealed: IDxgiAdapterProvider impl via Vortice.DXGI COM interop
│   │   │   ├── DxgiAdapterInfo.cs   # Internal record: 7 fields (Description, VendorId, Memory, etc.)
│   │   │   ├── MemoryStatusResult.cs # Internal record: 5 fields (MemoryLoad, Physical, PageFile)
│   │   │   ├── LhmSensorReading.cs  # Internal record struct + LhmHardwareType/LhmSensorType enums
│   │   │   ├── ILhmComputer.cs      # Internal interface: TryOpen() + ReadSensors() → IReadOnlyList<LhmSensorReading>
│   │   │   ├── LhmComputerWrapper.cs # Internal sealed: ILhmComputer impl via LibreHardwareMonitor (CPU+GPU sensors, not RAM)
│   │   │   ├── IPerfCounterProvider.cs # Internal interface: ReadCpuUsage/ReadAvailableRamMb/ReadPagesPerSecond + IDisposable
│   │   │   └── PerfCounterProvider.cs # Internal sealed: IPerfCounterProvider impl via System.Diagnostics.PerformanceCounter
│   │   ├── Monitoring/          # Runtime monitoring implementations (Nexus.Hardware.Windows.Monitoring)
│   │   │   ├── LhmSensorMonitor.cs  # Internal sealed: ISensorMonitor + IDisposable via ILhmComputer (Task.Run for 10-50ms LHM reads)
│   │   │   └── PerfCounterMonitor.cs # Internal sealed: IDisposable, synchronous ReadSnapshot() → SystemHealthSnapshot
│   │   └── Profilers/           # Hardware profiler implementations (Nexus.Hardware.Windows.Profilers)
│   │       ├── WmiCpuProfiler.cs    # Internal sealed: ICpuProfiler via WMI + SIMD intrinsics
│   │       ├── Win32RamProfiler.cs  # Internal partial: IRamProfiler via P/Invoke GlobalMemoryStatusEx
│   │       ├── DxgiGpuProfiler.cs   # Internal sealed: IGpuProfiler via IDxgiAdapterProvider
│   │       └── WindowsHostProfiler.cs # Public sealed: IHostProfiler compositor — concurrent CPU/RAM/GPU via Task.WhenAll, ProfileSafe<T> fallbacks, ClassifyArchitecture
│   ├── Nexus.Models/            # LLM model domain (candidates, profiles, catalog)
│   │   ├── Enums/               # Domain enums (Nexus.Models.Enums)
│   │   │   ├── ModelFormat.cs       # GGUF, SafeTensors, ONNX, OllamaManaged
│   │   │   ├── BackendRuntime.cs    # LlamaCpp, OllamaRuntime, OnnxRuntime
│   │   │   ├── ModelTaskFit.cs      # Chat, Reasoning, Coding
│   │   │   ├── CpuCostClass.cs      # Low, Medium, High, VeryHigh
│   │   │   ├── GpuCostClass.cs      # None, Low, Medium, High
│   │   │   ├── InferenceSpeedClass.cs # Fast, Moderate, Slow, VerySlow
│   │   │   ├── QualityTier.cs       # Basic, Good, Strong, Premium
│   │   │   ├── InteractionPreference.cs # LowLatency, Balanced, DeepReasoning, BatchProcessing
│   │   │   ├── OutputPreference.cs  # MaxSpeed, Balanced, MaxQuality, MaxStability
│   │   │   ├── PromptLength.cs      # Short, Medium, Long, VeryLong
│   │   │   ├── ResponseLength.cs    # Short, Medium, Long, VeryLong
│   │   │   ├── MultilingualRequirement.cs # None, Basic, Strong
│   │   │   ├── DistributionSource.cs # [Flags] Ollama=1, HuggingFace=2
│   │   │   ├── InstallComplexity.cs # Low, Medium, High
│   │   │   └── CompatibleArchitecture.cs # x64, ARM64
│   │   ├── ICuratedCatalog.cs      # Interface: Count, GetAllCandidates(), GetById(), GetByFamily(), GetByTaskFit()
│   │   ├── CuratedCatalog.cs      # Sealed: loads embedded curated-catalog.json (20 models) via Assembly.GetManifestResourceStream, indexes by Id/Family/TaskFit, immutable/thread-safe
│   │   ├── IModelNormalizer.cs     # Interface: Normalize(ModelCandidate) → ModelExecutionProfile
│   │   ├── ModelNormalizer.cs     # Stateless normalizer: quantization→bpp mapping (20 entries), memory estimation (weight/RAM/KV/VRAM), cost/quality classifiers, arch/runtime determination. Internal static helpers via InternalsVisibleTo.
│   │   ├── ModelServiceCollectionExtensions.cs # Public static: AddNexusModels() — registers ICuratedCatalog (Singleton) + IModelNormalizer (Singleton)
│   │   ├── ModelCandidate.cs      # 12-param record: primary model entity (Id, Family, Variant, Quantization, Format, params, size, context, backends, tasks, langs, DistributionProfile) + ToString()
│   │   ├── Data/
│   │   │   └── curated-catalog.json # EmbeddedResource: 20 real LLM model entries (Qwen=7, Gemma=3, Phi=2, Llama=4, Mistral=1, DeepSeek=3), camelCase props, PascalCase enums
│   │   └── Profiles/             # Immutable profile records (Nexus.Models.Profiles)
│   │       ├── DistributionProfile.cs  # 8-param record: download sources, tags, size, complexity
│   │       ├── ModelExecutionProfile.cs # 10-param record: RAM/VRAM, cost classes, quality, runtime
│   │       └── WorkloadIntentProfile.cs # 9-param record: intent, interaction/output prefs, prompt/response length, language, multilingual + static Default()
│   ├── Nexus.Recommendation/   # Decision engine (gates, scoring, ranking)
│   ├── Nexus.Distribution/     # Model download from sources
│   ├── Nexus.ModelRegistry/    # Local installed model tracking
│   └── Nexus.Runtime/          # Runtime abstractions (model + registry integration)
│
├── tests/
│   ├── Nexus.Memory.Tests/      # Memory layer tests
│   ├── Nexus.Core.Tests/        # Core orchestration tests
│   ├── Nexus.Integration.Tests/ # End-to-end tests + ToolComplexityClassifierTests (18 tests: +patch_ prefix, null description, malformed schema, null InputSchema) + ToolCapabilityResolverTests (13 tests) + ToolPromptFormatterTests (12 tests: +null InputSchema rendering) + McpToolExecutorFilteringTests (5 tests: disabled/null-formatter/empty-model fallback, happy path, empty tools) + PromptBuilderTests (12 tests: includes 2 model-name-forwarding tests for tool filtering wiring)
│   ├── Nexus.Desktop.Tests/     # Desktop ViewModel tests (Avalonia.Headless.XUnit)
│   ├── Nexus.Hardware.Tests/    # Hardware tests: enums, envelopes, profile, classifier, WmiCpuProfiler, Win32RamProfiler, DxgiGpuProfiler, WindowsHostProfiler, LhmSensorMonitor, PerfCounterMonitor, DI registration [Trait("Category","Integration")], records (164 tests)
│   └── Nexus.Models.Tests/      # Model domain tests: 15 enum tests, DistributionProfile (5), ModelExecutionProfile (4), ModelCandidate (7), WorkloadIntentProfile (7), ModelNormalizer (18), CuratedCatalog (15), DI registration (6) — 77 tests
│
├── docs/                        # Documentation
│   ├── user-requirements.md
│   ├── sprint-1.md
│   └── nexus-agent-documento-completo.md
│
├── nexus.yaml.example           # Example configuration
└── NexusAgent.slnx              # Solution file
```

---

## Design Principles

1. **La memoria ES el producto.** All design decisions prioritize the knowledge graph
2. **Usar, no construir.** Leverage existing frameworks and NuGet packages
3. **Local-first.** Must work 100% offline with Ollama. Cloud is optional
4. **Every LLM component allows local OR cloud.** Always provide both paths
5. **Mantenible por una persona.** No microservices, no over-engineering
6. **Interface segregation.** Define interfaces (IEmbeddingService, ILlmProvider) for swappable implementations

---

## Key Conventions

### DI Registration
Core services in `src/Nexus.Core/ServiceCollectionExtensions.cs` via `AddNexusAgent()`.
MCP services in `src/Nexus.Connectors/McpServiceCollectionExtensions.cs` via `AddNexusMcp()`:
```csharp
// IEmbeddingService uses DI factory for provider selection (ollama | openai)
services.AddSingleton<IEmbeddingService>(sp => config.Embeddings.Provider == "openai"
    ? new OpenAiEmbeddingService(options, apiKey)
    : new OllamaEmbeddingService(options));
services.AddSingleton<ILlmClient, OllamaLlmClient>(); // Cross-layer: interface in Memory, impl in Core
// KnowledgeGraph registered as both IKnowledgeGraph and IActionLogNotifier (same instance, Sprint 4 Day 2)
var knowledgeGraph = new KnowledgeGraph(dbInit.ConnectionString);
services.AddSingleton<IKnowledgeGraph>(knowledgeGraph);
services.AddSingleton<IActionLogNotifier>(knowledgeGraph);

// ILlmProvider multi-registration (Sprint 2): each provider registered separately
// API keys resolved via config.Models.GetApiKey("provider") — 3-tier fallback:
//   Tier 1: models.gemini.api_key (dedicated section)
//   Tier 2: models.cloud.api_key (legacy, when cloud.provider matches)
//   Tier 3: GEMINI_API_KEY env var
services.AddSingleton<ILlmProvider>(sp => new OllamaLlmProvider(config.Models.Local)); // always
services.AddSingleton<ILlmProvider>(sp => new GeminiLlmProvider(key, ...));    // if GetApiKey("gemini")
services.AddSingleton<ILlmProvider>(sp => new AnthropicLlmProvider(key, ...)); // if GetApiKey("anthropic")
services.AddSingleton<ILlmProvider>(sp => new OpenAiLlmProvider(key, ...));    // if GetApiKey("openai")
services.AddSingleton<LlmProviderFactory>(); // resolves all ILlmProvider via IEnumerable
services.AddSingleton<IInteractionSummarizer, InteractionSummarizer>(); // LLM summary + heuristic fallback
services.AddSingleton(sp => new EntityResolver(
    sp.GetRequiredService<IKnowledgeGraph>(),
    sp.GetService<IEmbeddingService>(),
    sp.GetService<ILlmClient>(),
    config.Memory.DeduplicationThreshold,
    sp.GetService<ILogger<EntityResolver>>())); // Entity dedup: find + merge duplicates
services.AddSingleton(sp => new MemoryCompressor(
    sp.GetRequiredService<IKnowledgeGraph>(),
    ConfigLoader.GetArchivePath(config),
    config.Memory.ArchiveThresholdDays,
    sp.GetService<ILogger<MemoryCompressor>>())); // Archive stale entities to JSON
services.AddSingleton(sp => new PromptBuilder(
    sp.GetRequiredService<MemoryContextBuilder>(), config.Agent,
    sp.GetService<IToolExecutor>()));  // Optional IToolExecutor for tool definitions in prompt
// ContextWindowManager: estimates token usage, compacts conversation history when approaching model context window limit
services.AddSingleton(sp => new ContextWindowManager(
    sp.GetRequiredService<IInteractionSummarizer>(),
    sp.GetRequiredService<PromptBuilder>(),
    config.Memory,
    sp.GetService<ILogger<ContextWindowManager>>()));
// AgentService receives ContextWindowManager? (optional) — compacts history before each LLM call
// IAgentService forwarding registration (Sprint 4 Day 3): Desktop resolves IAgentService, CLI resolves AgentService
services.AddSingleton<IAgentService>(sp => sp.GetRequiredService<AgentService>());

// MCP connectivity (registered separately via AddNexusMcp()):
services.AddSingleton<McpClientManager>();  // MCP SDK client (stdio + SSE transports)
services.AddSingleton<ToolRegistry>();      // Dynamic tool registry from MCP servers
services.AddSingleton<IToolComplexityClassifier>(sp =>
    new ToolComplexityClassifier(sp.GetService<ILogger<ToolComplexityClassifier>>())); // Stateless tool schema scorer + debug logging
services.AddSingleton(sp =>
    new ToolPromptFormatter(
        sp.GetRequiredService<IToolComplexityClassifier>(),
        sp.GetService<ILogger<ToolPromptFormatter>>()));  // Filters/annotates tools per model capability tier + info/debug logging
services.AddSingleton<IToolExecutor>(sp => new McpToolExecutor(
    sp.GetRequiredService<IMcpClientManager>(), ...,
    sp.GetRequiredService<ToolPromptFormatter>(),
    config.Mcp.ToolFilteringEnabled)); // Cross-layer: interface in Core, impl in Connectors; depends on IMcpClientManager abstraction. GetToolDefinitionsForPrompt(modelName) delegates to ToolPromptFormatter when filtering enabled
services.AddSingleton<ISchemaValidator>(sp => new SchemaValidator(...)); // Validates tool args against InputSchema (required, types, coercion)
```

### Configuration Model
`src/Nexus.Core/Config/NexusConfig.cs` — loaded from `nexus.yaml` via YamlDotNet:
```csharp
public class NexusConfig
{
    public AgentConfig Agent { get; set; } = new();
    public ModelsConfig Models { get; set; } = new();
    public EmbeddingsConfig Embeddings { get; set; } = new();
    public MemoryConfig Memory { get; set; } = new();
    public McpConfig Mcp { get; set; } = new();
    public UiConfig Ui { get; set; } = new();
}
// ModelsConfig has: Local, Cloud, Routing, Gemini?, Anthropic?, OpenAi?
// Per-provider keys: models.gemini.api_key, models.anthropic.api_key, models.openai.api_key
// Resolved via ModelsConfig.GetApiKey("provider") — 3-tier fallback
// McpConfig has: List<McpServerEntry> Servers, MaxToolCallIterations (int, default 3), ToolCallTimeoutSeconds (int, default 30), SchemaValidationEnabled (bool, default true), TypeCoercionEnabled (bool, default true), MaxOutputLines (int, default 200), MaxOutputBytes (int, default 32000), ToolFilteringEnabled (bool, default false — gates small-model tool complexity filtering)
// McpServerEntry has: Name, Transport ("stdio"|"sse"), Command?, Args (List<string>), Url?, Env (Dict<string,string>)
// ModelProviderConfig has: Provider, Model, Endpoint?, ApiKey?, ContextWindow (int, default 8192), MaxOutputTokens (int, default 2048)
// MemoryConfig has: SummarizationInterval (int, default 10), RecentInteractionsFetchLimit (int, default 5), DeduplicationThreshold (double, default 0.85), ArchivePath (string, default "~/.nexus/archive/"), CompressionEnabled (bool, default true), ArchiveThresholdDays (int, default 90), ContextCompactionThreshold (double, default 0.80), CompactionKeepRecentMessages (int, default 4)
```

### Database
SQLite with schema managed by `DatabaseInitializer.cs`. Tables: `entities`, `relations`, `interactions`, `agent_actions`.

### Testing
- xUnit for all tests
- Moq / NSubstitute for mocking interfaces
- In-memory SQLite for database tests
- Mock HttpMessageHandler for HTTP tests

### Build & Test Commands
```bash
dotnet build                                          # Build all
dotnet test                                           # Run all tests
dotnet test tests/Nexus.Memory.Tests/                 # Run specific project
dotnet test --filter "FullyQualifiedName~ClassName"   # Run specific class
dotnet run --project src/Nexus.CLI -- chat            # Run CLI
```

---

## Codebase Scan Patterns

When searching the existing codebase:
```
Glob: src/Nexus.Memory/**/*.cs      — All memory layer files
Glob: src/Nexus.Core/**/*.cs        — All core layer files
Glob: src/Nexus.Connectors/**/*.cs  — All connector files
Glob: src/Nexus.Desktop/**/*.cs     — All desktop UI files
Glob: src/Nexus.CLI/**/*.cs         — CLI files
Glob: src/Nexus.Hardware/**/*.cs   — Hardware contracts (enums, interfaces)
Glob: src/Nexus.Models/**/*.cs     — Model domain types
Grep: "interface I"                 — Find existing interfaces
Grep: "class.*Service"              — Find existing services

Namespace structure (Sprint 4 Day 6 reorg):
  Nexus.Core:    Abstractions/ Providers/ Services/ Models/ Config/
  Nexus.Memory:  Abstractions/ Embedding/ Graph/ Processing/ Infrastructure/ Models/
  Nexus.Models:  Enums/ Profiles/
Grep: "TODO|HACK|STUB"             — Find incomplete work
```
