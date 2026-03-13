# Tester Agent Memory

## Project Facts
- Solution: NexusAgent.slnx, .NET 10, TFM net10.0
- Test projects: Nexus.Memory.Tests, Nexus.Core.Tests, Nexus.Integration.Tests
- Test framework: xUnit + hand-rolled fakes (no Moq/NSubstitute used in practice)
- Naming convention: MethodName_Scenario_ExpectedResult

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

## Known Formatting Issues
- dotnet format --verify-no-changes exits with code 2 (pre-existing formatting violations)
- Violations are CRLF/indentation whitespace in: KnowledgeGraph.cs, SemanticSearch.cs,
  RelevanceDecay.cs, Program.cs, McpClientManager.cs, DatabaseInitializer.cs, etc.
- One sprint-introduced violation: MemoryContextBuilder.cs line 89 (trailing space on blank line)
- These are pre-existing debt — not introduced by the sprint under test
- Do NOT fail a sprint for pre-existing format violations; note them as known debt

## SQLite / Windows Disposal Pattern
- Always call SqliteConnection.ClearAllPools() before File.Delete() in Dispose()
- Always include GC.SuppressFinalize(this) in Dispose()
- EntityExtractorTests and MemoryContextBuilderTests both use file-based SQLite (temp path + Guid)

## Fake/Mock Patterns Used
- FakeEmbeddingService: tracks CallCount and CalledWithTexts[], configurable fixed embedding or exception
- MockLlmClient (inline in EntityExtractorTests): tracks LastPrompt, delegates to Func<string, Task<string>>
- Integration tests use real DI container (ServiceCollection + AddNexusAgent) with in-memory SQLite db path

## AC-11 / AC-12 Status (Sprint 1 Day 3)
- AC-11 covered by E2EFlowTests.BugFix001_AgentMaintainsConversationHistory — validates contract only
  (ConversationHistory starts empty, is IReadOnlyList). Full ChatAsync accumulation still needs Ollama.
- AC-12 covered by E2EFlowTests.BugFix002_ExtractionPromptRequiresEnglishOutput — asserts prompt
  contains "ALL output MUST be in English". PASS.
- Treat AC-11 as PARTIAL (automated contract check passes; full conversation accumulation manual)
