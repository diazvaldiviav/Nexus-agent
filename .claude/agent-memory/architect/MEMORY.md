# Architect Agent Memory

## Key Architecture Facts

- **Dependency flow:** Core -> Memory (via ProjectReference). Memory does NOT reference Core. Never reverse this.
- **Cross-layer interface pattern:** Interface in Memory, Implementation in Core. Used by: IEmbeddingService, ILlmClient.
- **EmbeddingsConfig** is in `Nexus.Core.Config`. Memory layer uses `EmbeddingOptions` record to avoid cross-layer dep.
- **No mocking library** in test projects. Tests use hand-written mocks (MockHandler, MockLlmClient).
- **All services are concrete classes** (no interfaces) except IEmbeddingService and ILlmClient.
- **Constructor-injected HttpClient** pattern used in OllamaEmbeddingService and OllamaLlmClient (for testability).

## Interfaces (2 total)
- `IEmbeddingService` (Nexus.Memory) -> `OllamaEmbeddingService` (Nexus.Memory, impl uses EmbeddingOptions)
- `ILlmClient` (Nexus.Memory) -> `OllamaLlmClient` (Nexus.Core, impl uses ModelProviderConfig)

## Project References (verified)
- `Nexus.Core.csproj` -> `Nexus.Memory.csproj` (Core depends on Memory)
- `Nexus.Memory.csproj` has NO project references (only SQLite, YamlDotNet packages)
- `Nexus.Memory.Tests.csproj` -> `Nexus.Memory.csproj` (no Moq/NSubstitute)

## DI Pattern
- All registration in `ServiceCollectionExtensions.AddNexusAgent()`
- Uses factory lambdas: `services.AddSingleton(sp => new ...)` for complex constructors
- Config sections registered as singletons: `services.AddSingleton(config.Embeddings)`
- Registration order matters: ModelRouter before ILlmClient before EntityExtractor

## File Locations
- DI registration: `src/Nexus.Core/ServiceCollectionExtensions.cs`
- Config model: `src/Nexus.Core/Config/NexusConfig.cs`
- Agent loop: `src/Nexus.Core/AgentService.cs` (uses static HttpClient)
- Entity extraction: `src/Nexus.Memory/EntityExtractor.cs` (3-level fallback: LLM -> Gemini -> heuristic)
- Prompt building: `src/Nexus.Core/PromptBuilder.cs`
- DB schema: `src/Nexus.Memory/DatabaseInitializer.cs`

## Test Patterns
- File-based SQLite with `SqliteConnection.ClearAllPools()` + `File.Delete()` in Dispose
- `GC.SuppressFinalize(this)` in all test Dispose methods
- Hand-rolled mocks for simple interfaces (ILlmClient = 1 method, use Func<string, Task<string>>)

## Decisions Log
- See: decisions.md for detailed rationale
- US-1.2: Gemini fallback as private method in EntityExtractor (not separate class) -- YAGNI
- US-1.2: extractionPrompt as optional param for backward compat
- US-1.2: KnowledgeGraph.GetEntityByNameAsync uses SQL LOWER() for case-insensitive match
