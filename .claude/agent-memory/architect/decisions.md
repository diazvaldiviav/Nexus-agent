# Architecture Decisions

## AD-001: EmbeddingOptions record in Nexus.Memory (US-1.1)
- **Problem:** OllamaEmbeddingService needs config (endpoint, model, dimensions) but Memory cannot reference Core
- **Decision:** Create `EmbeddingOptions` record in Nexus.Memory; DI maps EmbeddingsConfig -> EmbeddingOptions
- **Alternative rejected:** Passing primitives (endpoint, model) -- less cohesive, harder to extend

## AD-002: Constructor-injected HttpClient (US-1.1)
- **Problem:** Static HttpClient (AgentService pattern) prevents testing with mock handlers
- **Decision:** Optional HttpClient in constructor: `HttpClient? httpClient = null`, defaults to new instance with 30s timeout
- **Rationale:** Singleton DI lifetime means one instance anyway; test can inject mock

## AD-003: Hand-written MockHandler for tests (US-1.1)
- **Problem:** No Moq/NSubstitute in Nexus.Memory.Tests
- **Decision:** Hand-written `MockHandler : HttpMessageHandler` with `Func<HttpRequestMessage, Task<HttpResponseMessage>>`
- **Rationale:** 5 tests don't justify adding a dependency; pattern is reusable for future HTTP service tests
