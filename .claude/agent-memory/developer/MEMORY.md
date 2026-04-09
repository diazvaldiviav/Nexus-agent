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
- 3 test projects: Nexus.Memory.Tests (103 tests), Nexus.Core.Tests (153 tests, 1 pre-existing env-var failure), Nexus.Integration.Tests (23 tests)
- Total: 279 tests; 1 pre-existing failure (GetApiKey_ReturnsNull_WhenNoKeyAvailable — GEMINI_API_KEY env var is set on this machine)

## Key Files
- DI registration: `src/Nexus.Core/ServiceCollectionExtensions.cs`
- MCP DI registration: `src/Nexus.Connectors/McpServiceCollectionExtensions.cs`
- DB schema: `src/Nexus.Memory/DatabaseInitializer.cs`
- Config: `src/Nexus.Core/Config/NexusConfig.cs`
- LLM client interface: `src/Nexus.Memory/ILlmClient.cs`
- LLM client impl: `src/Nexus.Core/OllamaLlmClient.cs`
- Entity extraction: `src/Nexus.Memory/EntityExtractor.cs`
- Tool executor interface: `src/Nexus.Core/IToolExecutor.cs`
- MCP client manager: `src/Nexus.Connectors/McpClientManager.cs`
- Tool registry: `src/Nexus.Connectors/ToolRegistry.cs`
- MCP tool executor: `src/Nexus.Connectors/McpToolExecutor.cs`
- Schema validator interface: `src/Nexus.Core/Abstractions/ISchemaValidator.cs`
- Schema validator impl: `src/Nexus.Connectors/SchemaValidator.cs`

## Patterns Learned
- Raw string literals with `$"""` cannot have `{{` literal braces. Use `$$"""` with `{{interpolation}}` instead.
- Nexus.Memory has `InternalsVisibleTo("Nexus.Memory.Tests")` for testing internal static methods.
- DI registration order matters: ModelRouter -> ILlmClient -> EntityExtractor (dependency chain).
- `ILlmClient` uses `sp.GetService<>()` (nullable) not `sp.GetRequiredService<>()` to match nullable constructor.
- MCP SDK v1.1.0: Uses `McpClient.CreateAsync()` (not McpClientFactory). `HttpClientTransport` with `HttpTransportMode.AutoDetect` for SSE/StreamableHttp. `StdioClientTransport` for stdio. `CallToolResult.IsError` is `bool?`. `EnvironmentVariables` is `IDictionary<string, string?>`.
- Connectors -> Core dependency (one-way). McpServerEntry lives in Core Config. IToolExecutor lives in Core.
- In C# switch expressions, `Array` is unreachable after `IList` because `Array` implements `IList`. Use only `IList` to match both arrays and lists.

## Sprint Progress
- Sprint 1 Day 1: COMPLETE (embedding service)
- Sprint 1 Day 2 (US-1.2): COMPLETE (LLM entity extraction with 3-level fallback)
- Sprint 1 Day 3: COMPLETE (memory context builder + semantic search + decay)
- Sprint 1 Day 4: E2E tests + DI factory tests + hotfix validation (AC-1,2,3,8,9,11,12)
- Sprint 2 Day 3 (US-2.4 AC-1-4): COMPLETE (MCP SDK integration, IToolExecutor, McpClientManager rewrite, ToolRegistry update)
- Sprint 3 Day 5 (US-3.2 AC-4,5,6,7,9): COMPLETE (CompressSummariesAsync, RelevanceDecay archival hook, AgentService background archival, CLI archive/compress commands, 7 new tests). 170 tests total.
- Sprint 3 Day 6 (US-3.3 AC-1-7): COMPLETE (MCP persistence + CLI disconnect/servers commands, 5 new tests). 175 tests total.
- Schema Validation (AC-1-6): COMPLETE (ISchemaValidator + SchemaValidator + AgentService integration + DI + config flags, 11 new tests). 186 tests total (excluding pre-existing env-var failure).
