# Requirements Analyst - Persistent Memory

## Project Architecture
- **Dependency flow:** Interface (CLI/Desktop) -> Core -> Memory + Connectors. NEVER reverse.
- **No interfaces exist yet** in the codebase (all concrete classes). IEmbeddingService will be the FIRST.
- Nexus.Memory CANNOT reference Nexus.Core (would violate dependency flow). Use POCOs/records in Memory layer for config.

## Cross-Layer Config Pattern
- `EmbeddingsConfig` lives in `Nexus.Core.Config` but `OllamaEmbeddingService` lives in `Nexus.Memory`
- Solution: Create `EmbeddingOptions` record in Nexus.Memory, map from EmbeddingsConfig in DI registration
- This pattern should be used for ALL Memory-layer services that need config

## Existing Code Inventory (Sprint 1 baseline)
- **28 existing tests** across 3 test projects (9 KG, 4 Decay, 3 Search, 3 Config, 4 Router, 5 Integration)
- **No mocking library** in test .csproj files (no Moq, no NSubstitute) - must add or hand-roll mocks
- **Static HttpClient pattern** used in AgentService - follow same pattern with constructor injection for testability
- `SemanticSearch.ToByteArray()` and `ToFloatArray()` are public static - reuse for embedding conversion

## Key File Locations
- DI registration: `src/Nexus.Core/ServiceCollectionExtensions.cs`
- Config model: `src/Nexus.Core/Config/NexusConfig.cs`
- CLI entry: `src/Nexus.CLI/Program.cs`
- DB schema: `src/Nexus.Memory/DatabaseInitializer.cs`

## Sprint 1 Dependencies
- US-1.1 (EmbeddingService) BLOCKS US-1.3, US-1.4, US-1.5
- US-1.6 (Stabilize) can run in parallel with US-1.1

## Feature Analysis Artifacts
- [Context Window Compaction](project_context_compaction.md) — ContextWindowManager, config changes, AgentService 4-point integration
