# Skill: SOLID Principles — Nexus Agent (.NET 10)

> SOLID principles applied to the Nexus Agent C# codebase. Review before any design or architectural decision.

---

## S — Single Responsibility Principle (SRP)

**Rule:** One class = one reason to change.

### GOOD: Separated Concerns

```csharp
// Service: only business logic for knowledge graph operations
public class KnowledgeGraph : IKnowledgeGraph
{
    public async Task<Entity?> GetEntityAsync(string name, CancellationToken ct = default) { ... }
    public async Task PersistEntityAsync(Entity entity, CancellationToken ct = default) { ... }
    public async Task<List<Relation>> GetRelationsAsync(long entityId, CancellationToken ct = default) { ... }
}

// Separate: embedding generation
public class OllamaEmbeddingService : IEmbeddingService
{
    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default) { ... }
}

// Separate: entity extraction from text
public class EntityExtractor
{
    public async Task<List<Entity>> ExtractAsync(string text, CancellationToken ct = default) { ... }
}

// Orchestrator: ties them together
public class AgentService
{
    private readonly IKnowledgeGraph _memory;
    private readonly IEmbeddingService _embeddings;
    private readonly EntityExtractor _extractor;
    // Orchestrates workflow, delegates to specialized services
}
```

### BAD: Mixed Concerns

```csharp
// BAD: One class handles memory + LLM + extraction + config
public class AgentMonolith
{
    public async Task<string> Chat(string message)
    {
        // Direct SQLite queries here
        var conn = new SqliteConnection(...);
        // Direct Ollama calls here
        var http = new HttpClient();
        // Regex entity extraction here
        var matches = Regex.Matches(response, pattern);
        // Config parsing here
        var yaml = File.ReadAllText("nexus.yaml");
    }
}
```

### Nexus Agent Guidelines

| Class | Single Responsibility |
|---|---|
| `AgentService` | Orchestrate the agent loop (memory → LLM → extract → persist) |
| `ModelRouter` | Select appropriate LLM provider based on task |
| `PromptBuilder` | Build prompts with memory context |
| `KnowledgeGraph` | SQLite CRUD for entities, relations, interactions |
| `SemanticSearch` | Embedding-based similarity search |
| `EntityExtractor` | Extract entities from text (LLM or heuristic) |
| `RelevanceDecay` | Apply time-based decay to relevance scores |
| `ConfigLoader` | Load and parse YAML configuration |
| `McpClientManager` | Manage MCP server connections |
| `ToolRegistry` | Register and discover available tools |
| `*ViewModel` | UI state + commands for one view |

---

## O — Open/Closed Principle (OCP)

**Rule:** Open for extension, closed for modification.

### GOOD: Extensible via Interfaces + DI

```csharp
// Adding a new LLM provider requires NO changes to existing code
public interface ILlmProvider
{
    Task<string> GenerateAsync(string prompt, CancellationToken ct = default);
    string ModelName { get; }
}

// Existing implementations untouched
public class OllamaLlmProvider : ILlmProvider { ... }
public class AnthropicLlmProvider : ILlmProvider { ... }

// New: just add a new class + DI registration
public class GoogleLlmProvider : ILlmProvider { ... }

// DI factory selects based on config — existing code unchanged
services.AddSingleton<ILlmProvider>(sp =>
{
    var config = sp.GetRequiredService<NexusConfig>();
    return config.Llm.Provider switch
    {
        "anthropic" => new AnthropicLlmProvider(config),
        "google" => new GoogleLlmProvider(config),  // New — just add case
        _ => new OllamaLlmProvider(config),
    };
});
```

### BAD: Requires Modification to Extend

```csharp
// BAD: Adding a provider requires modifying this class
public class LlmService
{
    public async Task<string> GenerateAsync(string prompt, string provider)
    {
        if (provider == "ollama") { /* ... */ }
        else if (provider == "anthropic") { /* ... */ }
        // Must add new else-if for every provider
    }
}
```

---

## L — Liskov Substitution Principle (LSP)

**Rule:** Implementations must honor interface contracts without breaking behavior.

### GOOD: All Implementations Honor Contracts

```csharp
public interface IEmbeddingService
{
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default);
}

// Both return float[] of the correct dimensions — substitutable
public class OllamaEmbeddingService : IEmbeddingService
{
    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        // Returns 768-dimensional vector from nomic-embed-text
        var result = await CallOllamaAsync(text, ct);
        return result.Embedding; // Always float[], never null
    }
}

public class OpenAiEmbeddingService : IEmbeddingService
{
    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        // Returns 1536-dimensional vector from text-embedding-3-small
        var result = await CallOpenAiAsync(text, ct);
        return result.Embedding; // Always float[], never null
    }
}
```

### BAD: Implementation Violates Contract

```csharp
// BAD: Returns null instead of empty array — breaks callers that don't check
public class BrokenEmbeddingService : IEmbeddingService
{
    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        return null!; // Violates contract — callers expect non-null array
    }
}
```

---

## I — Interface Segregation Principle (ISP)

**Rule:** Focused interfaces with few methods. Clients should not depend on methods they don't use.

### GOOD: Focused Interfaces

```csharp
// Each interface has a clear, focused purpose
public interface IEmbeddingService
{
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default);
}

public interface ILlmProvider
{
    Task<string> GenerateAsync(string prompt, CancellationToken ct = default);
    string ModelName { get; }
}

public interface IKnowledgeGraph
{
    Task<Entity?> GetEntityAsync(string name, CancellationToken ct = default);
    Task PersistEntityAsync(Entity entity, CancellationToken ct = default);
    Task<List<Entity>> GetRecentEntitiesAsync(int limit, CancellationToken ct = default);
}
```

### BAD: Fat Interface

```csharp
// BAD: One interface tries to cover everything
public interface IAiService
{
    Task<float[]> GenerateEmbeddingAsync(string text);
    Task<string> GenerateTextAsync(string prompt);
    Task<Entity?> GetEntityAsync(string name);
    Task PersistEntityAsync(Entity entity);
    Task<List<MCP.Tool>> GetToolsAsync();
    void ApplyDecay(double factor);
    NexusConfig GetConfig();
}
```

---

## D — Dependency Inversion Principle (DIP)

**Rule:** Depend on abstractions (interfaces), not concrete implementations. Inject via constructor.

### GOOD: Depend on Abstractions

```csharp
// AgentService depends on interfaces, not concrete classes
public class AgentService
{
    private readonly IKnowledgeGraph _memory;
    private readonly ILlmProvider _llm;
    private readonly IEmbeddingService _embeddings;

    public AgentService(
        IKnowledgeGraph memory,
        ILlmProvider llm,
        IEmbeddingService embeddings)
    {
        _memory = memory;
        _llm = llm;
        _embeddings = embeddings;
    }
}

// DI registration — swappable implementations
services.AddSingleton<IKnowledgeGraph, KnowledgeGraph>();
services.AddSingleton<ILlmProvider, OllamaLlmProvider>();
services.AddSingleton<IEmbeddingService, OllamaEmbeddingService>();
```

### BAD: Depend on Concrete Implementations

```csharp
// BAD: Hardcoded dependency on OllamaLlmProvider
public class AgentService
{
    private readonly OllamaLlmProvider _llm = new();  // Cannot swap for testing or cloud
    private readonly KnowledgeGraph _memory = new();   // Cannot mock for unit tests
}
```

---

## SOLID Quick Reference for Code Review

| Principle | ✅ Pass | ❌ Fail |
|---|---|---|
| **S**RP | Each class has one job | Class does memory + LLM + config |
| **O**CP | New provider = new class, no existing code changes | Must modify switch/if-else to add |
| **L**SP | All IEmbeddingService impls return valid float[] | Implementation returns null or throws unexpectedly |
| **I**SP | Focused interfaces: IEmbeddingService, ILlmProvider | One IAiService with 10+ methods |
| **D**IP | Constructor injection of interfaces | `new OllamaLlmProvider()` inside service |
