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

## Links to Detail Files
- patterns.md: recurring architecture patterns
