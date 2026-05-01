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
│   │   │   ├── IToolExecutor.cs    # Cross-layer (impl in Connectors). GetToolDefinitionsForPrompt() + GetToolDefinitionsForPrompt(string? modelName) default interface method for model-aware tool filtering + GetToolSchema(string toolName) => null DIM for schema-driven retry templates + GetToolDefinition(string toolName) => null DIM (Phase 9, returns ToolDefinition? for verifier arg introspection) + GetToolServerName(string toolName) => string.Empty DIM (Phase 9, returns server name for snapshot routing)
│   │   │   ├── IToolPlanner.cs     # Plan-then-execute orchestration: Task<ToolPlan?> GeneratePlanAsync(userMessage, toolDefinitionsForPrompt, ct) + 4-arg overload GeneratePlanAsync(userMessage, toolDefinitionsForPrompt, PlannerContext?, ct) (Phase 9 — context block injected before tool list when non-empty). Graceful-degradation contract — returns null on disabled flag, empty tools, LLM failure, unparseable output, all-null matches. OperationCanceledException rethrown.
│   │   │   ├── ISchemaValidator.cs  # Schema validation contract + SchemaValidationResult (impl in Connectors)
│   │   │   ├── IPlannerContextBuilder.cs # BuildAsync(IReadOnlyList<ConversationMessage>, string, CancellationToken) → Task<PlannerContext>. Heuristic compaction; never throws (catch all non-OCE → log + return Empty). OCE rethrown.
│   │   │   ├── IToolVerifier.cs         # VerifyAsync(server, tool, args, preSnapshot, toolResult, ct) + CapturePreSnapshotAsync(server, tool, args, ct). Both args are IReadOnlyDictionary<string,object>?. preSnapshot captured BEFORE toolResult. Co-located: VerificationOutcome (sealed class: bool IsVerified, bool RuleMatched, string? Reason, float Confidence + static factories Verified(string?), Failed(string, float=0.9f), NoRule()).
│   │   │   ├── IVerificationCatalog.cs  # Co-locates VerificationMethod enum (None, SnapshotDiff, ResponseShape, ResponseKeywords) + VerificationRule + SnapshotSpec + IVerificationCatalog interface (GetRule(server,tool), Count). VerificationRule.EmptyPostIsFailure at rule level (NOT in SnapshotSpec). Sprint 10 AC-2: VerificationRule adds bool Destructive { get; init; } (default false; backward-compat preserved).
│   │   │   └── IPermissionGate.cs       # Public interface Task<PermissionGateResponse> RequestAsync(PermissionRequest, CancellationToken). Co-locates: PermissionRequest (sealed record: ServerName, ToolName, Arguments IReadOnlyDictionary<string,object>?, Patterns IReadOnlyList<string>, Rationale string?), PermissionGateResponse (sealed record: Decision PermissionDecision, Feedback string?), PermissionFeedback (sealed record), PermissionDecision enum (5 values: Allow, AllowForSession, AllowPersisted, Deny, DenyWithFeedback). Note: RequestAsync returns PermissionGateResponse record (not bare enum) so DenyWithFeedback carries its feedback string — documented architectural deviation reconciling AC-3 enum spec with AC-5 pattern-match wire-in.
│   │   ├── Providers/           # LLM providers (Nexus.Core.Providers)
│   │   │   ├── OllamaLlmProvider.cs
│   │   │   ├── GeminiLlmProvider.cs
│   │   │   ├── AnthropicLlmProvider.cs
│   │   │   ├── OpenAiLlmProvider.cs
│   │   │   ├── OllamaLlmClient.cs  # ILlmClient impl via Ollama HTTP
│   │   │   └── LlmProviderFactory.cs
│   │   ├── Services/            # Orchestration (Nexus.Core.Services)
│   │   │   ├── AgentService.cs      # Main agent loop + output truncation + doom loop detection + plan-controlled execution path. Optional IToolPlanner? ctor param (8th, first optional after summarizer), optional IPlannerContextBuilder? (9th, Phase 9), optional IToolVerifier? (10th, Phase 9), optional IPermissionGate? (11th, Sprint 10 AC-5), optional IVerificationCatalog? (12th, Sprint 10 AC-5 — needed to read rule.Destructive). Plan gate in ChatAsync/ChatStreamAsync before existing tool loop: (AC-1) when Mcp.PlannerHeuristicEnabled, calls PlannerInvocationHeuristic.ShouldInvokePlanner(userMessage, config) → logs "[Planner] heuristic: shouldPlan={ShouldPlan} reason={Reason}" at Information; flag heuristicAllow defaults true (false short-circuits plan gate entirely); THEN when _toolPlanner + _toolExecutor + HasTools all present, calls GeneratePlanAsync → if Mcp.PlannerContextEnabled, calls _plannerContextBuilder?.BuildAsync → passes PlannerContext? to 4-arg GeneratePlanAsync overload → if plan non-null + Steps.Count > 0, dispatches to ExecutePlanAsync/ExecutePlanStreamAsync; else falls through to existing loop unchanged. ExecutePlanAsync: per step with MatchedToolName, bounded while-loop (up to config.Mcp.StepExecutionMaxAttempts, default 5) using BuildStepPrompt(attempt, step, schema): attempt 1 = natural-language "[PLANNER] Execute ONLY this step", attempt 2 = schema-driven fill-in-the-blanks template (DescribeRequired+BuildArgsTemplate helpers) or attempt-1 fallback when schema null, attempt 3+ = hard coercion ("Your previous response was prose"). Step-level Info log "Plan step {n}/{total} attempt {attempt}/{max}: tool={tool}". Per-step try/catch wraps ExecuteToolWithTimeoutAsync with "when (!ct.IsCancellationRequested)" filter + "[Tool {name} failed: {ex.Message}]" history entry + stepCompleted=true (AC-A1). Tool result format: "[Tool result for step N]\n{result}". On exhaustion: LogError + "[PlanStep N] Exceeded N attempts; moving on." sentinel. Truncates via OutputTruncator.Truncate. Final summary LLM call wrapped with linked CTS (ToolPlanningTimeoutSeconds) → graceful minimal AgentResponse on timeout. ExecutePlanStreamAsync mirrors with per-attempt progress tokens + enumerator-drain pattern on summary stream emitting "[Summary unavailable: {ex.GetType().Name}]" sentinel (pendingFallback pattern avoids CS1631). Named constants PlanTrailExtractionWindow=3 + PlanTrailHeaderSize=4. Static helpers: BuildStepPrompt, DescribeRequired, BuildArgsTemplate (adjacent to PlanTrail constants). GetToolSchema called via _toolExecutor!.GetToolSchema(step.MatchedToolName) at loop entry. AC-9: after ExecuteToolWithTimeoutAsync, when result starts with [VerificationWarning] AND Mcp.ToolVerificationEnabled AND attempt < maxAttempts → log warning, append history with retry message, attempt++, continue (counts toward StepExecutionMaxAttempts budget). ExecuteToolWithTimeoutAsync (AC-5): serverName hoisted early (shared by gate + verifier); schema/arg validation first; THEN permission gate consulted when _permissionGate != null && _config.Permission.Enabled && (rule?.Destructive == true || ResolveConfigAction(toolName) == "ask") → patterns via PermissionPatternExtractor.Extract → _permissionGate.RequestAsync → on Deny/DenyWithFeedback returns $"{SyntheticMarkers.PermissionDeniedPrefix}{reason}"; gate exceptions → log Warning + fall through (allow-by-default, arch §11.7); OperationCanceledException rethrown; THEN pre-snapshot via _toolVerifier.CapturePreSnapshotAsync → tool invoke → post-snapshot via VerifyAsync → decorate result "[VerificationWarning] {reason}\n{result}" when RuleMatched + !IsVerified. Snapshot OCE rethrown; other exceptions → log Warning + skip (not fatal). Private ResolveConfigAction(string toolName) helper: case-insensitive lookup _config.Permission.Tools[toolName].Action. (AC-6) SummaryFailureAnalyzer.Analyze(_conversationHistory) called in both ExecutePlanAsync (~L700) and ExecutePlanStreamAsync (~L902) BEFORE the final summarize user message; when findings.HasFailures, injects Role="user" grounding message via BuildGroundingMessage; logs "[PlanResult] {V} verification, {R} retries, {T} tool errors, {P} permission, {D} doom" at Information. Shared RunBackgroundExtraction private helper (SRP); [PLANNER]-prefixed messages filtered via StartsWith(Ordinal) (AC-A2)
│   │   │   ├── ContextWindowManager.cs # Context window estimation + conversation compaction
│   │   │   ├── ModelRouter.cs       # Local vs cloud selection
│   │   │   ├── OutputTruncator.cs   # Static: head/tail line truncation + UTF-8 safe byte truncation (TruncatedOutput record)
│   │   │   ├── PromptBuilder.cs     # Memory context + tool definitions. BuildSystemPromptAsync(userQuery, modelName?, ct) passes modelName to IToolExecutor for model-aware tool filtering. New: BuildPlanExecutionSystemPromptAsync(string userQuery, CancellationToken ct) for plan-execute mode (omits "call tools freely" instructions, appends step-execution directive). Shared private BuildPreludeAsync((StringBuilder, bool HasTools) return) helper extracted to DRY the identity+memory+tools-listing prelude between both public builders; reads model name internally from _config.Models.Local.Model. Optional NexusConfig? ctor param (4th) for plan-mode model-name inference; null-guards on required params.
│   │   │   ├── ToolCallParser.cs    # Multi-format tool call parser: [TOOL_CALL:] marker + <tool_call> XML + raw JSON fallback, markdown fence stripping, brace-walking state machine (WalkJsonObject 3-tuple with endedInString), mid-string JSON repair (closes unclosed quotes before appending braces), IsParsableJson guard, TryParseAll multi-tool extraction with ParsedToolCall position tracking + IsOverlapping dedup, TryParseJson shared helper
│   │   │   ├── PlannerContextBuilder.cs # Sealed: IPlannerContextBuilder impl. Heuristic: filter SyntheticMarkers prefixes → take last MaxRecentTurns → truncate per-turn at MaxBytesPerTurn UTF-8 bytes (append …) → extract paths (regex (?:[A-Z]:\\|/)[^\s"<>|]+, dedupe last 3) + last invoked tool from synthetic markers → cap total at MaxBytes. First turn / all-synthetic / cancellation → Empty. Never throws (non-OCE → log + return Empty). Private const Ellipsis = "…" (AC-H7, replaces inline literal). Logging (AC-H3): on non-empty result → LogDebug "[PlannerContext] built: paths={PathCount}, lastTool={Tool}, turns={TurnCount}, bytes={TotalBytes}"; on all-empty short-circuit → LogDebug "[PlannerContext] history yielded empty context".
│   │   │   ├── SyntheticMarkers.cs      # Internal static: Prefixes[] = 9 strings ("[PLANNER] ", "[Plan]", "[PlanStep ", "[Tool result for step ", "[Tool Result for ", "[Executing tool: ", "[DoomLoop]", "[VerificationWarning]", "[PermissionDenied]") + IsSynthetic(string?). Consumed by PlannerContextBuilder + AgentService.RunBackgroundExtraction. Public consts (AC-H7): VerificationWarningMarker = "[VerificationWarning]"; VerificationWarningPrefix = VerificationWarningMarker + " " (index 7 of Prefixes uses the const; AgentService StartsWith checks + decoration site consume these consts). Sprint 10 AC-5 consts: PermissionDeniedMarker = "[PermissionDenied]"; PermissionDeniedPrefix = "[PermissionDenied] " (index 8 of Prefixes; consumed by SummaryFailureAnalyzer + AgentService gate return).
│   │   │   ├── PlannerInvocationHeuristic.cs # Internal static. Method (bool ShouldPlan, string Reason) ShouldInvokePlanner(string userMessage, NexusConfig config). Deterministic 4-step algorithm: trim+min-length check (< PlannerHeuristicMinLength → false, "too_short") → de-accent+greeting-set check (normalized message in greeting HashSet → false, "chat_greeting"; greeting set: hola|hi|hello|hey|gracias|thanks|ok|vale|si|no|adios|bye|como estas|how are you|que tal|buenos dias|buenas|saludos|chao + ?, ??, …; strips trailing ?!.… before compare) → imperative-verb/path/file-extension regex triggers (any match → true, reason label) → default (true, "default_allow"). Pre-compiled static readonly regexes. Never throws — outer try/catch returns (true, "fallback_default_allow"). De-accent via NormalizationForm.FormD + strip combining marks (UnicodeCategory.NonSpacingMark).
│   │   │   ├── SummaryFailureAnalyzer.cs # Internal static. Sealed record Findings(int VerificationWarnings, int RetriesExhausted, int ToolErrors, int PermissionDenials, int DoomLoops, int StepsSkippedNoToolMatch, IReadOnlyList<string> ExcerptedReasons) with bool HasFailures computed property (any count > 0). Methods: Analyze(IReadOnlyList<ConversationMessage>?) (null-safe → empty Findings; single-pass over history with mutually-exclusive else-if sentinel detection using SyntheticMarkers consts; caps ExcerptedReasons at last 3 assistant-role excerpts truncated to 200 chars). Sprint 10 follow-up Layer 3: 6th category StepsSkippedNoToolMatch detects "[PlanStep " AND "No tool matched" AND "skipping" pattern emitted by AgentService.ExecutePlanAsync at lines 635/851 when MatchedToolName is null. BuildGroundingMessage(Findings) returns "" when !HasFailures; otherwise builds "[PlanResult]" block with non-zero counts only ("Steps skipped (no matching tool): N" line for new category) and reason excerpts; injected as Role="user" message before summarize prompt. Uses SyntheticMarkers.PermissionDeniedMarker (post AC-5 swap).
│   │   │   ├── PermissionPatternExtractor.cs # Internal static. Extract(string toolName, IReadOnlyDictionary<string,object>? arguments, VerificationRule? rule) → IReadOnlyList<string>. Walks SnapshotSpec.Args JSONPath VALUES (e.g. {"path": "$.path"} value = "$.path") reusing McpToolVerifier-style $.field/$.field[N] resolver against arguments dict. Common-key fallback (path, source, destination, file_path, filename) when rule == null or rule.Snapshot == null. Catch-all → ["*"]. Never throws.
│   │   │   ├── PersistentPermissionStore.cs # Public sealed class IDisposable. Manages ~/.nexus/permissions.json schema {version:1, directories: {<base64(cwd)>: {<tool>: {patterns: {<glob>: "allow"|"deny"}, updatedAt: ISO-8601}}}}. Atomic writes (temp+File.Move overwrite). SemaphoreSlim in-process lock around writes. LookupAsync(tool, pattern, ct) → string?("allow"|"deny"|null), UpsertAsync(tool, pattern, action, ct) → Task, AllowAsync (helper), ListAsync. Malformed JSON / IOException → log Warning + treat as empty store (TryLookup returns null; Upsert starts fresh). All async I/O uses ConfigureAwait(false). Path resolved at DI site in Program.cs.
│   │   │   ├── AutoApprovePermissionGate.cs # Public sealed class implements IPermissionGate. Tier-aware non-interactive gate (Desktop placeholder + tests). Inlined tier detection: private enum ToolTier { Limited, Capable, Full } + private regex @"(\d+(?:\.\d+)?)\s*b(?![a-z])" + constants LimitedModelThreshold=3.0, CapableModelThreshold=8.0 (character-identical to ToolCapabilityResolver.Resolve; cross-layer constraint prevents direct reference; Sprint 11 cleanup candidate IModelTierResolver). Full tier → Allow + LogWarning("non-interactive: auto-approving"); Limited|Capable → Deny with feedback "non-interactive prompt unavailable" + Information log.
│   │   │   └── ToolPlanner.cs       # Sealed IToolPlanner impl: plan-then-execute for small models (opt-in via Mcp.ToolPlanningEnabled). Constructor (LlmProviderFactory, NexusConfig, ILogger<ToolPlanner>?, IEmbeddingService? embeddingService = null) — 4 params (4th re-introduced by Sprint 10 follow-up Layer 2). Gates: !ToolPlanningEnabled → null; empty toolDefinitionsForPrompt → null. Uses config.Models.Local provider/model for planning LLM call (local-first). Named constants: MaxSteps=5, NormalizedMatchScore=0.9f, TokenOverlapThreshold=0.7f. Static TokenSeparators 14-char array. StepRegex: ^\s*(?:step\s*)?(\d+)[.):]\s*(.+) (IgnoreCase|Multiline|Compiled). Sprint 10 follow-up Layer 1: PlanningPromptTemplate revised — imperative formatting "Each step MUST start with: Use `tool_name` to ..." + GOOD/BAD few-shot examples + explicit prohibition of natural-language verbs ("Insert", "Save", "Add", "Modify", "Update", "Edit") without tool name. Step parsing truncated to MaxSteps. LLM call wrapped in linked CTS (caller ct + config.Mcp.ToolPlanningTimeoutSeconds seconds) — timeout returns null via "when (!ct.IsCancellationRequested)" filter, caller cancellation propagates. ct.ThrowIfCancellationRequested() checkpoints between LLM call+parse and per matching iteration. Deterministic 3-tier fuzzy match: Tier 1 case-insensitive Contains → Similarity 1.0f; Tier 2 Normalize() (underscore/hyphen → space + lowercase) Contains → Similarity 0.9f; Tier 3 Tokenize() token overlap ratio ≥ TokenOverlapThreshold → Similarity ratio; strict > argmax (earlier tool wins ties). Sprint 10 follow-up Layer 2: Tier 4 embedding fallback — when Tier 1-3 returns matched=null AND _embeddings != null AND Mcp.ToolPlannerEmbeddingFallbackEnabled (default true) → MatchStepWithEmbeddingsAsync(step, tools, ct) computes step description embedding, compares against lazy-cached tool embeddings (Dictionary<string, float[]> guarded by SemaphoreSlim, populated once per planner instance from "name: description" via ExtractTools FullLine output), picks max cosine similarity if ≥ Mcp.ToolPlannerEmbeddingMatchThreshold (default 0.65f, range 0.40-0.95 validator-enforced). Local CosineSimilarity helper (10 LOC, mirrors SemanticSearch.CosineSimilarity to avoid Nexus.Core → Nexus.Memory.Graph cycle). Gracefully degrades: any exception other than OCE → log Warning "[DIAG-P9] embedding fallback failed for step N" + return step unchanged. Helpers: Normalize() + Tokenize() (splits on TokenSeparators). Cross-layer dep on Nexus.Memory.Abstractions re-introduced (Sprint 10 follow-up Layer 2 — already used elsewhere in Nexus.Core for MemoryCompressor/InteractionSummarizer). All awaits use ConfigureAwait(false). Graceful degradation: outer try/catch — OperationCanceledException rethrown; other exceptions log warning + return null. XML <remarks> documents all graceful-null triggers.
│   │   ├── Models/              # POCOs (Nexus.Core.Models)
│   │   │   ├── AgentResponse.cs
│   │   │   ├── ConversationMessage.cs
│   │   │   ├── ToolPlan.cs          # Two sealed records co-located: ToolPlanStep(int StepNumber, string Description, string? MatchedToolName, float Similarity) + ToolPlan(IReadOnlyList<ToolPlanStep> Steps, string RawPlanText). Transient only — never persisted to DB.
│   │   │   └── PlannerContext.cs    # Sealed record(string Summary, IReadOnlyList<string> RecentTurns, int TotalBytes). Empty static factory + IsEmpty + ToPromptBlock() — Markdown "## Conversation Context" header + "Working on: {Summary}" + "Recent turns:" bulleted; returns "" when IsEmpty (byte-equivalent to Phase 8 prompt with no context).
│   │   ├── Config/
│   │   │   ├── ConfigLoader.cs
│   │   │   ├── NexusConfig.cs
│   │   │   └── ConfigValidator.cs    # Static validation: Memory + Models + MCP config (scalar ranges, McpServerEntry transport/url). Sprint 10 additions: validates PlannerHeuristicMinLength [1,200], PathValidatorStrictDistance [50,100]; walks Permission.Tools dict and validates Action + Patterns values are in {allow|ask|deny} (case-insensitive); error message "PermissionToolRule.Action must be 'allow', 'ask', or 'deny' (got '{action}').".
│   │   └── ServiceCollectionExtensions.cs # DI registration (stays at root). Sprint 10 AC-5: AgentService factory now resolves permissionGate: sp.GetService<IPermissionGate>() and verificationCatalog: sp.GetService<IVerificationCatalog>() (both optional via GetService<>).
│   │
│   ├── Nexus.Connectors/        # External tool connectivity (MCP SDK)
│   │   ├── McpClientManager.cs  # MCP client: stdio/SSE transport, tool discovery, invocation
│   │   ├── ToolRegistry.cs      # Dynamic tool registry (ConcurrentDictionary, thread-safe) + ToolResolution record + ResolveTool() fuzzy name resolution (exact → case-insensitive → Levenshtein ≤2 → fail)
│   │   ├── McpToolExecutor.cs   # IToolExecutor impl: depends on IMcpClientManager (not concrete), routes tool calls through MCP, uses ResolveTool() for fuzzy name matching. GetToolDefinitionsForPrompt(string? modelName) override: when ToolFilteringEnabled + modelName non-empty → delegates to ToolPromptFormatter.Format(); otherwise falls back to unfiltered ToolRegistry output. GetToolSchema(string toolName) override: calls _toolRegistry.ResolveTool(toolName) and returns resolution.Tool?.InputSchema. GetToolDefinition(string toolName) override (Phase 9): returns ToolDefinition? via _toolRegistry.ResolveTool(toolName).Tool. GetToolServerName(string toolName) override (Phase 9): returns resolved server name from _toolRegistry.ResolveTool(toolName).ServerName (empty string on miss)
│   │   ├── SchemaValidator.cs   # ISchemaValidator impl: validates tool args against InputSchema (required check, type coercion string→bool/number/array, unknown arg stripping)
│   │   ├── McpServiceCollectionExtensions.cs # AddNexusMcp() DI extension
│   │   ├── PathValidator.cs     # Validates and corrects file/dir paths against allowed-directories catalog. Sprint 10 AC-7: Existence-wins guard (STEP A): if File.Exists(normalized) || Directory.Exists(normalized) → return PathCheckResult(true, normalized, false, null) BEFORE fuzzy correction (eliminates stale-state false positives). Strict-distance gate (STEP B): when originalWithinAllowed && distance < _strictDistance (default 80, empirically calibrated: Bug 4 scores 60-70, legitimate typos score 80-95) → return failure with suggestions (rejects spurious fuzzy corrections for in-bounds missing paths). Cross-root corrections (origin outside allowed dirs) still use fuzzy threshold (80) for legitimate use cases. New FindBestMatchWithScore(string, List<CatalogEntry>, out int) overload; legacy FindBestMatch delegates. Logs: "[PathValidator] stale-state guard: original '{Raw}' missing; rejected '{Match}' (score {Score} < strict {Strict})". _strictDistance field read from config.Mcp.PathValidatorStrictDistance at construction. Sprint 10 follow-up — basename-uniqueness short-circuit (FindBestMatchWithScore exact-match branch): when exactMatches.Count == 1 → distance = 100 (treat as max confidence, bypass strict gate); when count > 1 → keep full-path Fuzz.Ratio so strict gate still disambiguates. Rationale: a unique basename in the catalog has nothing to disambiguate (e.g. "nexus/ecomerce" → single "D:\Nexus\ecomerce" dir accepts even with low full-path score from CWD-prefix divergence); ambiguous basenames (many index.html files — actual Bug 4 scenario) keep the strict gate active.
│   │   ├── ToolFiltering/       # Tool complexity classification for small-model filtering
│   │   │   ├── ToolComplexityTier.cs        # Enum: Simple, Moderate, Complex
│   │   │   ├── ToolCallingTier.cs           # Enum: Limited, Capable, Full (model capability tier)
│   │   │   ├── ToolComplexityScore.cs       # Record: 7-field scoring result (ToolName, Score, Tier, RequiredParamCount, TotalParamCount, MaxNestingDepth, HasArrayOfObjects)
│   │   │   ├── IToolComplexityClassifier.cs # Interface: Classify(ToolDefinition) → ToolComplexityScore
│   │   │   ├── ToolComplexityClassifier.cs  # Sealed classifier: weighted score formula (0.15*req+0.08*total+0.25*depth+0.35*arrayOfObj+0.05*enum+0.15*semantic+0.05*optExcess), named constants (SimpleTierThreshold=0.50, ModerateTierThreshold=0.80, MaxNestingDepthCap=5), null-safe Description/Name access, debug logging after score computation
│   │   │   ├── ToolCapabilityResolver.cs   # Static: Resolve(string? modelName) → ToolCallingTier via regex param-count extraction, named constants (LimitedModelThreshold=3.0, CapableModelThreshold=8.0), safe default Full
│   │   │   └── ToolPromptFormatter.cs    # Sealed: Format(tools, modelName) → filtered prompt string. ILogger support. Delegates tool rendering to ToolRegistry.RenderToolToStringBuilder(). Combines ToolComplexityClassifier + ToolCapabilityResolver to partition tools into included (with optional hints) and excluded (with 3-tier BuildExclusionHint: WorkflowOverrides → same-server Simple → fallback). Full-tier parity with ToolRegistry.GetToolDefinitionsForPrompt()
│   │   └── Catalog/             # MCP tool verification catalog (Phase 9)
│   │       ├── VerificationCatalog.cs    # Sealed: IVerificationCatalog impl, loads embedded YAMLs + ~/.nexus/catalog/ overrides; ctor(NexusConfig, ILogger?, optional internal overrideDir for tests via InternalsVisibleTo). Private const DefaultUserCatalogDir = ".nexus/catalog" (AC-H7). Logging (AC-H2): ctor emits exactly one LogInformation "[Catalog] loaded {BundledCount}+{OverrideCount} rules from bundled+override sources ({TotalRules} effective)" after Merge.
│   │       ├── VerificationCatalogLoader.cs # Internal static: LoadFromEmbeddedResources + LoadUserOverrides + Merge (last-write-wins per (server.ToLower, tool.ToLower)). Malformed YAML → log Warning + skip. Private consts (AC-H7): MethodSnapshotDiff = "snapshot_diff", MethodResponseShape = "response_shape", MethodResponseKeywords = "response_keywords" (used in ParseMethod switch). Logging (AC-H6): ParseMethod logs LogWarning "[Catalog] unknown verification method '{Method}' — falling back to None (rule will be skipped at runtime)" for non-null/non-whitespace unrecognized strings; ToVerificationRules logs LogWarning "[Catalog] tool '{Server}/{Tool}' declares method=snapshot_diff but has no snapshot block — rule skipped" and yields nothing for that tool. Sprint 10 AC-2: CatalogYamlTool DTO adds bool Destructive { get; set; } (YAML key destructive: via UnderscoredNamingConvention); wired through ToVerificationRules mapping to VerificationRule.Destructive.
│   │       ├── McpToolVerifier.cs        # Sealed: IToolVerifier impl. SnapshotDiff (re-invoke snapshot tool with JSONPath args, compare not_equal/different_size, EmptyPostIsFailure short-circuit), ResponseShape (JSON path validation), ResponseKeywords (case-insensitive substring scan). Snapshot calls wrapped in linked CTS with VerificationSnapshotTimeoutSeconds. OCE rethrown; other exceptions → Failed(reason, 0.5f). Private const SnapshotContentKey = "content" (AC-H7) — used in InvokeSnapshotAsync, ExtractContent, and all snapshot payload dict accesses; zero remaining "content" string literals outside the const.
│   │       ├── filesystem.yaml          # EmbeddedResource: 13 verification rules (was 9). Sprint 10 AC-2: 4 new destructive rules with method: response_keywords — move_file, delete_file, delete_directory, move_directory. Existing write_file + edit_file tagged destructive: true (snapshot config unchanged). YAML uses name:, args_from:, empty_post_is_failure at tool level.
│   │       └── memory.yaml              # EmbeddedResource: placeholder (server: memory, tools: [])
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
│   │   │   ├── SettingsViewModel.cs  # Settings MVVM: ConfigValidator integration, IsDirty/SettingsSnapshot dirty tracking (19-field record), CanSave guard, inline validation errors (Memory + MCP fields), ApiKeyWarning, HasError/HasSuccess banners, MCP tool settings (MaxToolCallIterations, ToolCallTimeoutSeconds, MaxOutputLines, MaxOutputBytes, SchemaValidationEnabled, ToolFilteringEnabled, ToolPlanningEnabled) with reactive OnChanged validation
│   │   │   └── ActionLogViewModel.cs  # Action log VM: HasActions computed property, DispatchToUI virtual
│   │   ├── Layout/
│   │   │   └── ForceDirectedLayout.cs  # Fruchterman-Reingold force-directed graph layout
│   │   └── Controls/
│   │       ├── GraphCanvas.cs   # Custom graph rendering control with cached ImmutableBrush/Pen, nodeLookup cache
│   │       ├── MarkdownRenderer.cs  # Static helper: markdown string → IReadOnlyList<Control> via Markdig AST (Catppuccin Mocha palette, DisableHtml security)
│   │       └── MarkdownTextBlock.cs  # UserControl: StyledProperty<string?> Text, 250ms DispatcherTimer debounce, attach/detach lifecycle
│   │   └── App.axaml.cs         # Avalonia app entry + DI setup. Sprint 10 AC-4: DI placeholder services.AddSingleton<IPermissionGate, AutoApprovePermissionGate> (until full Avalonia permission dialog ships in a future sprint).
│   │
│   ├── Nexus.CLI/               # Terminal interface
│   │   ├── OnboardingWizard.cs  # First-use setup wizard: 7-step (Ollama, chat model, embed model, API keys, MCP filesystem, config gen, save with overwrite protection)
│   │   ├── CliPermissionGate.cs # Public sealed class implements IPermissionGate. Spectre.Console two-stage prompt. Decision precedence: persistent-deny → small-model guard (skips persistent-allow + session for non-Full tier) → persistent-allow (Full-tier only) → in-memory _sessionAllowed HashSet (Full-tier only) → _config.Permission.Tools[tool] (per-pattern map first, simple Action otherwise) → interactive prompt. Choice labels use [[a]]/[[s]]/[[p]]/[[d]]/[[r]] (Spectre.Console markup escape — outer [[ renders as literal [). Small-model: 3 options ([a]/[d]/[r]); Full-tier: 5 options + ESC. Stage 2 ([r]) uses TextPrompt<string>.AllowEmpty() — empty defaults to "user denied". Persist failures emit visible red MarkupLine before falling back to session. Destructive rationale (keywords "destructive"/"delete"/"overwrite") changes panel header to "[red bold]Permission Required — DESTRUCTIVE[/]" + red rationale color. Small-model warning rendered as standalone MarkupLine after panel (Spectre 0.49.x lacks Panel.Subtitle).
│   │   └── Program.cs           # Spectre.Console chat loop + memory/connect/disconnect/servers/init commands. Sprint 10 AC-4: DI registers services.AddSingleton<PersistentPermissionStore> (with ~/.nexus/permissions.json resolved path) and services.AddSingleton<IPermissionGate, CliPermissionGate>.
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
│   ├── Nexus.Core.Tests/        # Core orchestration tests + ToolPlannerTests (14 tests: 8 parsing/gates/timeout tests unchanged — disabled/empty gates, numbered + Step N parsing, >5 truncation, garbage → null, LLM call timeout, more-than-MaxSteps truncation+log — PLUS 6 fuzzy-match tests: Tier1_ExactToolNameInDescription, Tier1_CaseInsensitive, Tier2_NormalizedUnderscores, Tier3_TokenOverlap, NoMatch_ReturnsNullMatched, FullPlanning_FakeLlm_ReturnsValidPlanWithFuzzyMatches) + ConfigValidatorTests (+5 new from Phase 8.3: ToolPlanningEnabled_RequiresLocalModel, ToolPlanningEnabled_AllowsDefaultConfig, ToolPlanningTimeoutSeconds_RangeValidated [Theory], StepExecutionMaxAttempts_RangeValidated [Theory 5 values], StepExecutionMaxAttempts_DefaultValue_IsFive) + PlannerContextBuilderTests (12 tests [was 7]: Phase 9 original 7 — filters synthetic, truncates per-turn, caps total, all-synthetic → Empty, single turn, cancellation → OCE, never-throws contract — PLUS 5 new AC-H8 edge-cases: HistoryWithUrls_DoesNotMatchUrlsAsPaths, PathEndingWithDot_TrimsTrailingPunctuation, PathEndingWithCloseParen_TrimsTrailingPunctuation, SurrogatePairAtTruncationBoundary_DoesNotSplitChar, NonEmptyResult_LogsDebugSummary; note: 1 test marked [Fact(Skip)] for documented v1 URL-regex over-match limitation, 11 effectively passing) + ConfigValidatorTests additions (8 new for Phase 9: PlannerContext 4 range fields + VerificationSnapshotTimeoutSeconds range + defaults) + ToolPlannerTests additions (2 new: context block injected into prompt, null-context byte-equivalence) + VerificationCatalogTests (NEW unit-test file, 3 tests: Constructor_LogsLoadSummary_AtInformationLevel, UnknownMethodString_LogsWarning_AndSkipsRule, SnapshotDiffWithoutSnapshotBlock_LogsWarning_AndSkipsRule — covers AC-H2 and AC-H6 via spy logger) + McpToolVerifierTests (NEW unit-test file, 2 tests: VerifyResponseShape_InvalidJsonResult_ReturnsFailedWithReason, ResolveJsonPathArgs_NullJsonPathValue_ReturnsNullAndLogsDebug — covers AC-H8 edge cases). Phase 9.1 unit delta: +10 unit tests total (+5 PlannerContextBuilder, +3 VerificationCatalog, +2 McpToolVerifier). Sprint 10 additions: PlannerInvocationHeuristicTests (16 [Theory] cases across 5 test methods), PermissionConfigTests (5 tests), PermissionPatternExtractorTests (9 tests), AutoApprovePermissionGateTests (2 tests), SummaryFailureAnalyzerTests (5 tests) + VerificationCatalogTests (+3 for AC-2 Destructive field) + PathValidatorTests (+3 AC-7 tests: existence-wins guard, strict-distance rejection, strict-distance acceptance — PLUS 2 follow-up basename-uniqueness tests: Validate_UniqueBasename_AcceptsLowFullPathScore_WithinAllowedDirs, Validate_AmbiguousBasename_StillRequiresStrictDistance; Validate_OriginalMissing_StrictDistanceRejected_WhenScoreBelowThreshold modified to create 2 index.html files for ambiguous-basename Bug-4-faithful repro) + ConfigValidatorTests (+11 across AC-1/AC-3/AC-7: PlannerHeuristicMinLength range, PathValidatorStrictDistance range, Permission.Tools Action validation). Sprint 10 total: Nexus.Core.Tests ~376 tests (1 pre-existing env-var failure).
│   ├── Nexus.Integration.Tests/ # End-to-end tests + ToolComplexityClassifierTests (18 tests: +patch_ prefix, null description, malformed schema, null InputSchema) + ToolCapabilityResolverTests (13 tests) + ToolPromptFormatterTests (12 tests: +null InputSchema rendering) + McpToolExecutorFilteringTests (5 tests: disabled/null-formatter/empty-model fallback, happy path, empty tools) + PromptBuilderTests (12 tests: includes 2 model-name-forwarding tests for tool filtering wiring) + AgentServicePlanExecutionTests (13 tests: 5 original — plan executes steps in order, retry on missing tool call, model calls different tool executes anyway, disabled → normal loop, no matched tools → fall through — PLUS 4 hardening tests — tool-execution-throws plan continues, [PLANNER] messages filtered from extraction, cancellation mid-step propagates OCE, streaming summary LLM failure emits sentinel "[Summary unavailable: {TypeName}]" — PLUS 4 Phase 8.3 bounded-retry tests — SucceedsOnAttempt2_SchemaTemplateInjected, SucceedsOnAttempt3_CoercionPrompt, ExceedsMaxAttempts_LogsErrorAndSkips, NoSchemaAvailable_FallsBackToAttempt1PromptStyle) + AgentServicePlannerContextTests (2 tests: Phase 9) + VerificationCatalogTests (10 tests — Integration.Tests file unchanged by Phase 9.1; 3 additional unit tests now live in Nexus.Core.Tests) + McpToolVerifierTests (10 tests — Integration.Tests file unchanged by Phase 9.1; 2 additional unit tests now live in Nexus.Core.Tests) + AgentServiceVerificationTests (5 tests) + AgentServicePlanVerificationRetryTests (2 tests). AC-H1 backward-compat audit: Phase 9 flags Mcp.PlannerContextEnabled and Mcp.ToolVerificationEnabled default ON. Pre-Phase-9 tests in AgentServicePlanExecutionTests, McpToolCallLoopTests, DoomLoopTests, DeduplicationIntegrationTests, and E2EFlowTests either (a) explicitly set config.Mcp.PlannerContextEnabled = false; config.Mcp.ToolVerificationEnabled = false; at all config-construction sites where exact tool-call counts / prompt body / result strings are asserted (Phase-8.3-era assertions sensitive to Phase 9 decoration), or (b) carry // AC-H1: verified compatible with Phase 9 defaults-ON comment where only final-result text or step success/failure is asserted. Integration test count unchanged: 0 new integration tests in Phase 9.1. Sprint 10 additions: CliPermissionGateTests (6 tests), AgentServicePermissionGateTests (9 tests), AgentServiceSummaryHardeningTests (2 tests). Pre-existing plan-execution test factories updated to set Mcp.PlannerHeuristicEnabled=false in 3 files (AgentServicePlanExecutionTests, AgentServicePlanVerificationRetryTests, AgentServicePlannerContextTests); StepExecution_ExceedsMaxAttempts_LogsErrorAndSkips predicate updated (Contains → StartsWith+Contains). Sprint 10 total: Nexus.Integration.Tests ~187 tests (1 pre-existing DI failure — Ollama not running).
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
// Plan-then-execute for small models — IToolPlanner registered after LlmProviderFactory (opt-in via config.Mcp.ToolPlanningEnabled):
services.AddSingleton<IToolPlanner>(sp => new ToolPlanner(
    sp.GetRequiredService<LlmProviderFactory>(),
    config,
    sp.GetService<ILogger<ToolPlanner>>())); // AgentService receives it as optional 8th ctor param
// Phase 9: IPlannerContextBuilder — registered in Core ServiceCollectionExtensions:
services.AddSingleton<IPlannerContextBuilder>(sp =>
    new PlannerContextBuilder(config, sp.GetService<ILogger<PlannerContextBuilder>>())); // AgentService 9th ctor param
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
    sp.GetService<IToolExecutor>(), config));  // Optional IToolExecutor for tool definitions; optional NexusConfig? enables BuildPlanExecutionSystemPromptAsync to read config.Models.Local.Model internally
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
// Phase 9: IVerificationCatalog must be registered BEFORE IToolVerifier (verifier depends on catalog):
services.AddSingleton<IVerificationCatalog>(sp => new VerificationCatalog(config, logger)); // Loads embedded YAMLs + ~/.nexus/catalog/ overrides
services.AddSingleton<IToolVerifier>(sp => new McpToolVerifier(...)); // AgentService 10th ctor param; depends on IVerificationCatalog + IMcpClientManager

// Sprint 10 AC-4 (Nexus.CLI Program.cs only):
services.AddSingleton<PersistentPermissionStore>(sp => new PersistentPermissionStore(
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nexus", "permissions.json"),
    sp.GetService<ILogger<PersistentPermissionStore>>())); // file path resolved at DI site
services.AddSingleton<IPermissionGate, CliPermissionGate>(); // AgentService 11th ctor param (via GetService<>)

// Sprint 10 AC-4 (Nexus.Desktop App.axaml.cs only — placeholder):
services.AddSingleton<IPermissionGate, AutoApprovePermissionGate>(); // until Avalonia permission dialog ships
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
    public PermissionConfig Permission { get; set; } = new(); // Sprint 10 AC-3
}
// ModelsConfig has: Local, Cloud, Routing, Gemini?, Anthropic?, OpenAi?
// Per-provider keys: models.gemini.api_key, models.anthropic.api_key, models.openai.api_key
// Resolved via ModelsConfig.GetApiKey("provider") — 3-tier fallback
// McpConfig has: List<McpServerEntry> Servers, MaxToolCallIterations (int, default 3), ToolCallTimeoutSeconds (int, default 30), SchemaValidationEnabled (bool, default true), TypeCoercionEnabled (bool, default true), MaxOutputLines (int, default 200), MaxOutputBytes (int, default 32000), ToolFilteringEnabled (bool, default false — gates small-model tool complexity filtering), ToolPlanningEnabled (bool, default false — gates plan-then-execute path for small models; opt-in via nexus.yaml mcp.tool_planning_enabled), ToolPlanningTimeoutSeconds (int, default 30, range 5..300 — linked-CTS timeout for planner LLM call + plan-mode final summary; validator-enforced), StepExecutionMaxAttempts (int, default 5, range 1..20 — per-step bounded retry ceiling in ExecutePlanAsync/ExecutePlanStreamAsync; validator-enforced), PlannerContextEnabled (bool, default true — gates planner context injection; Phase 9), PlannerContextMaxBytes (int, default 1500, range 200..16000 — max total UTF-8 bytes for context block), PlannerContextMaxRecentTurns (int, default 4, range 1..20 — max non-synthetic turns included), PlannerContextMaxBytesPerTurn (int, default 280, range 80..4000 — per-message truncation limit), ToolVerificationEnabled (bool, default true — gates pre/post snapshot verification; Phase 9), VerificationSnapshotTimeoutSeconds (int, default 10, range 1..60 — per-snapshot call timeout), PlannerHeuristicEnabled (bool, default true — AC-1; gates chat/greeting heuristic BEFORE planner invocation in ChatAsync/ChatStreamAsync), PlannerHeuristicMinLength (int, default 16, range 1..200, validator-enforced — AC-1; minimum message length to even attempt planning), PathValidatorStrictDistance (int, default 80, range 50..100, validator-enforced — AC-7; strict similarity threshold used when original path is missing-but-within-allowed-dirs. Empirically calibrated: Bug 4 silent stale-state corruption scores 60-70 on full-path Fuzz.Ratio while legitimate typo+relative-path corrections score 80-95 — threshold 80 separates them cleanly. Combines with basename-uniqueness short-circuit in FindBestMatchWithScore for unambiguous matches), ToolPlannerEmbeddingFallbackEnabled (bool, default true — Sprint 10 follow-up Layer 2; gates Tier-4 embedding fallback in ToolPlanner when lexical 3-tier matcher returns null. Requires IEmbeddingService registered in DI; absent service → no-op fall-through), ToolPlannerEmbeddingMatchThreshold (float, default 0.65f, range 0.40..0.95, validator-enforced — Sprint 10 follow-up Layer 2; minimum cosine similarity for embedding fallback to accept a tool match. Below threshold → step.MatchedToolName remains null → AgentService skip sentinel emitted → SummaryFailureAnalyzer's StepsSkippedNoToolMatch detects it)
// McpServerEntry has: Name, Transport ("stdio"|"sse"), Command?, Args (List<string>), Url?, Env (Dict<string,string>)
// Permission (PermissionConfig) — top-level config section (Sprint 10 AC-3): PermissionConfig { bool Enabled = true; Dictionary<string,PermissionToolRule> Tools }. PermissionToolRule { string? Action; Dictionary<string,string>? Patterns }. Action ∈ {"allow"|"ask"|"deny"} case-insensitive, validated by ConfigValidator. Patterns key = glob pattern, value = "allow"|"deny".
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
