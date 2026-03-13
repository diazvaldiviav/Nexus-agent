# Developer Agent Memory

## Project Environment
- .NET SDK 10.0.103 installed (as of 2026-03-05), runtime 10.0.3
- All projects updated to `net10.0` TFM (were originally net9.0)
- No global.json in repo

## Known Issues & Fixes
- **SQLite file lock on Windows**: Microsoft.Data.Sqlite uses connection pooling. In test Dispose(), must call `SqliteConnection.ClearAllPools()` before `File.Delete()` or the file remains locked and xUnit marks the test as FAILED.
- For integration tests that use ServiceProvider, dispose the SP first, then clear pools, then delete file.

## Project Structure
- Solution: `NexusAgent.slnx`
- 5 src projects: Nexus.Memory, Nexus.Core, Nexus.Connectors, Nexus.CLI, Nexus.Desktop
- 3 test projects: Nexus.Memory.Tests (52 tests), Nexus.Core.Tests (14 tests), Nexus.Integration.Tests (13 tests)
- Total: 79 tests, all passing

## Key Files
- DI registration: `src/Nexus.Core/ServiceCollectionExtensions.cs`
- DB schema: `src/Nexus.Memory/DatabaseInitializer.cs`
- Config: `src/Nexus.Core/Config/NexusConfig.cs`
- LLM client interface: `src/Nexus.Memory/ILlmClient.cs`
- LLM client impl: `src/Nexus.Core/OllamaLlmClient.cs`
- Entity extraction: `src/Nexus.Memory/EntityExtractor.cs`

## Patterns Learned
- Raw string literals with `$"""` cannot have `{{` literal braces. Use `$$"""` with `{{interpolation}}` instead.
- Nexus.Memory has `InternalsVisibleTo("Nexus.Memory.Tests")` for testing internal static methods.
- DI registration order matters: ModelRouter -> ILlmClient -> EntityExtractor (dependency chain).
- `ILlmClient` uses `sp.GetService<>()` (nullable) not `sp.GetRequiredService<>()` to match nullable constructor.

## Sprint Progress
- Sprint 1 Day 1: COMPLETE (embedding service)
- Sprint 1 Day 2 (US-1.2): COMPLETE (LLM entity extraction with 3-level fallback)
- Sprint 1 Day 3: COMPLETE (memory context builder + semantic search + decay)
- Sprint 1 Day 4: E2E tests + DI factory tests + hotfix validation (AC-1,2,3,8,9,11,12)
