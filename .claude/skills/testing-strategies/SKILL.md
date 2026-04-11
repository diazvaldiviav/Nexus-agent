# Skill: Testing Strategies — Nexus Agent (.NET 10)

> Complete testing playbook for the Nexus Agent .NET application. Consult before writing any test.

---

## Test Stack

| Tool | Purpose |
|---|---|
| **xUnit** | Test framework (test discovery, assertions, fixtures) |
| **Moq** or **NSubstitute** | Mocking interfaces for unit tests |
| **Microsoft.Data.Sqlite** | In-memory SQLite for database tests |
| **System.Net.Http** | Mock HttpMessageHandler for HTTP tests |

---

## Test Organization

```
tests/
├── Nexus.Memory.Tests/          # Memory layer unit tests
│   ├── KnowledgeGraphTests.cs
│   ├── SemanticSearchTests.cs
│   ├── RelevanceDecayTests.cs
│   ├── EntityExtractorTests.cs
│   └── MemoryContextBuilderTests.cs
│
├── Nexus.Core.Tests/             # Core orchestration unit tests
│   ├── ModelRouterTests.cs
│   ├── PromptBuilderTests.cs
│   ├── ConfigLoaderTests.cs
│   └── AgentServiceTests.cs
│
└── Nexus.Integration.Tests/      # End-to-end integration tests
    └── AgentIntegrationTests.cs
```

**Rules:**
- One test file per source file (mirrors `src/` structure)
- File naming: `[ClassName]Tests.cs`
- Test project naming: `[ProjectName].Tests`
- Group related tests with nested classes or `[Trait]`

---

## Test Naming Convention

```
MethodName_Scenario_ExpectedResult
```

```csharp
[Fact]
public async Task GetEntityAsync_EntityExists_ReturnsEntity() { ... }

[Fact]
public async Task GetEntityAsync_EntityNotFound_ReturnsNull() { ... }

[Fact]
public async Task GenerateEmbeddingAsync_ValidText_ReturnsCorrectDimensions() { ... }

[Fact]
public async Task GenerateEmbeddingAsync_EmptyText_ThrowsArgumentException() { ... }

[Fact]
public async Task ApplyDecay_AfterOneDay_ReducesScore() { ... }
```

---

## AAA Pattern (Arrange / Act / Assert)

```csharp
[Fact]
public async Task GetEntityAsync_EntityExists_ReturnsEntity()
{
    // Arrange
    var graph = CreateKnowledgeGraph();
    await graph.PersistEntityAsync(new Entity { Name = "C#", Type = "Technology" });

    // Act
    var result = await graph.GetEntityAsync("C#");

    // Assert
    Assert.NotNull(result);
    Assert.Equal("C#", result.Name);
    Assert.Equal("Technology", result.Type);
}
```

---

## Mocking Interfaces

### With Moq

```csharp
[Fact]
public async Task ChatAsync_ReturnsLlmResponse()
{
    // Arrange
    var mockLlm = new Mock<ILlmProvider>();
    mockLlm.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync("Hello! I'm Nexus.");
    mockLlm.Setup(x => x.ModelName).Returns("test-model");

    var mockMemory = new Mock<IKnowledgeGraph>();
    var mockEmbeddings = new Mock<IEmbeddingService>();
    mockEmbeddings.Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new float[768]);

    var agent = new AgentService(mockMemory.Object, mockLlm.Object, mockEmbeddings.Object);

    // Act
    var response = await agent.ChatAsync("Hi");

    // Assert
    Assert.Equal("Hello! I'm Nexus.", response.Text);
    mockLlm.Verify(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
}
```

### With NSubstitute

```csharp
[Fact]
public async Task ChatAsync_ReturnsLlmResponse()
{
    // Arrange
    var llm = Substitute.For<ILlmProvider>();
    llm.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
       .Returns("Hello! I'm Nexus.");
    llm.ModelName.Returns("test-model");

    var memory = Substitute.For<IKnowledgeGraph>();
    var embeddings = Substitute.For<IEmbeddingService>();
    embeddings.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
              .Returns(new float[768]);

    var agent = new AgentService(memory, llm, embeddings);

    // Act
    var response = await agent.ChatAsync("Hi");

    // Assert
    Assert.Equal("Hello! I'm Nexus.", response.Text);
    await llm.Received(1).GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
}
```

---

## In-Memory SQLite for Database Tests

```csharp
public class KnowledgeGraphTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly KnowledgeGraph _graph;

    public KnowledgeGraphTests()
    {
        // In-memory SQLite — shared connection keeps DB alive for test duration
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        // Initialize schema
        DatabaseInitializer.Initialize(_connection);

        _graph = new KnowledgeGraph(_connection);
    }

    [Fact]
    public async Task PersistEntity_ThenRetrieve_ReturnsCorrectData()
    {
        // Arrange
        var entity = new Entity { Name = "Nexus", Type = "Project", Summary = "AI Agent" };

        // Act
        await _graph.PersistEntityAsync(entity);
        var result = await _graph.GetEntityAsync("Nexus");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Nexus", result.Name);
        Assert.Equal("Project", result.Type);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
```

---

## Mock HttpMessageHandler for HTTP Tests

```csharp
public class OllamaEmbeddingServiceTests
{
    [Fact]
    public async Task GenerateEmbedding_ReturnsCorrectDimensions()
    {
        // Arrange
        var embedding = new float[768];
        var responseJson = JsonSerializer.Serialize(new { embedding });

        var handler = new MockHttpMessageHandler(responseJson);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:11434")
        };

        var config = new NexusConfig
        {
            Embeddings = new EmbeddingsConfig
            {
                Endpoint = "http://localhost:11434",
                Model = "nomic-embed-text"
            }
        };

        var service = new OllamaEmbeddingService(config, httpClient);

        // Act
        var result = await service.GenerateEmbeddingAsync("test input");

        // Assert
        Assert.Equal(768, result.Length);
    }
}

// Reusable mock handler
public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly string _responseContent;
    private readonly HttpStatusCode _statusCode;

    public MockHttpMessageHandler(string responseContent, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _responseContent = responseContent;
        _statusCode = statusCode;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage
        {
            StatusCode = _statusCode,
            Content = new StringContent(_responseContent, Encoding.UTF8, "application/json")
        });
    }
}
```

---

## Test Categories

### 1. Unit Tests — Services

Test business logic in isolation with mocked dependencies.

```csharp
// What to test:
// - Happy path (valid input → correct output)
// - Edge cases (empty input, null, boundary values)
// - Error handling (service unavailable, invalid response)
// - Each interface method
```

### 2. Unit Tests — Models

Test model construction and default values.

```csharp
[Fact]
public void Entity_DefaultValues_AreCorrect()
{
    var entity = new Entity();
    Assert.Equal("", entity.Name);
    Assert.Equal(1.0, entity.RelevanceScore);
    Assert.Null(entity.Embedding);
}
```

### 3. Integration Tests — Database

Test SQLite operations with real in-memory database.

```csharp
// What to test:
// - CRUD operations work correctly
// - Schema initialization creates all tables
// - Parameterized queries prevent SQL injection
// - Concurrent reads work (WAL mode)
```

### 4. Integration Tests — Agent Flow

Test the full agent pipeline with mocked external services.

```csharp
[Fact]
public async Task AgentLoop_UserMessage_ExtractsAndPersistsEntities()
{
    // Arrange: mock LLM, real in-memory DB
    // Act: send user message through AgentService
    // Assert: entities extracted and persisted in knowledge graph
}
```

---

## Test Quality Checklist

| Check | Pass | Fail |
|---|---|---|
| All tests pass | 0 failures | Any failure |
| No skipped tests | 0 skipped (or justified) | Unexplained skips |
| External deps mocked | Tests don't need Ollama running | Tests fail without Ollama |
| AAA pattern | Arrange/Act/Assert clearly separated | Unclear structure |
| Edge cases | Error paths and null inputs tested | Only happy path |
| AC coverage | Every AC has at least 1 test | AC without test coverage |
| Naming convention | `Method_Scenario_Expected` | Vague test names like `Test1` |
| Deterministic | Tests pass independently, any order | Tests depend on each other |
| Fast | Unit tests < 1s each | Tests take > 5s (hitting real services) |

---

## Verification Commands

```bash
# Build (must be clean)
dotnet build

# Run all tests
dotnet test --verbosity normal

# Run specific test project
dotnet test tests/Nexus.Memory.Tests/
dotnet test tests/Nexus.Core.Tests/
dotnet test tests/Nexus.Integration.Tests/

# Run specific test class
dotnet test --filter "FullyQualifiedName~KnowledgeGraphTests"

# Run specific test method
dotnet test --filter "FullyQualifiedName~GetEntityAsync_EntityExists_ReturnsEntity"

# Run with detailed output
dotnet test --filter "FullyQualifiedName~[TestName]" --logger "console;verbosity=detailed"
```
