# Skill: Coding Standards — Nexus Agent (.NET 10)

> Mandatory coding standards for all C# code in the Nexus Agent project. Load when writing any code.

---

## 1. C# Language Standards

### Nullable Reference Types
```csharp
// GOOD: Explicit nullable annotations
public string? OptionalName { get; set; }
public string RequiredName { get; }

// GOOD: Null-coalescing operators
var displayName = user?.DisplayName ?? "Anonymous";

// GOOD: Null guard in constructor
public MyService(IEmbeddingService embeddingService)
{
    _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
}

// BAD: Suppressing nullable warning without justification
var name = user!.DisplayName; // Avoid unless guaranteed non-null
```

### Async/Await
```csharp
// GOOD: All I/O methods are async with CancellationToken
public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
{
    var response = await _http.PostAsync(url, content, ct);
    response.EnsureSuccessStatusCode();
    var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(ct);
    return result?.Embedding ?? Array.Empty<float>();
}

// GOOD: ConfigureAwait(false) in library code (non-UI)
var data = await _http.GetStringAsync(url, ct).ConfigureAwait(false);

// BAD: Sync over async — blocks thread pool
var result = service.GetDataAsync().Result;  // NEVER
var result2 = service.GetDataAsync().GetAwaiter().GetResult(); // NEVER
```

### Type Annotations
```csharp
// GOOD: Explicit return types on all public methods
public async Task<List<Entity>> GetEntitiesAsync(string query, CancellationToken ct = default) { ... }

// GOOD: Explicit parameter types
public async Task UpdateScoreAsync(string entityId, double newScore) { ... }
```

### Naming Conventions

| Element | Convention | Example |
|---|---|---|
| Classes | PascalCase | `KnowledgeGraph` |
| Interfaces | I + PascalCase | `IEmbeddingService` |
| Public methods | PascalCase + Async suffix | `GenerateEmbeddingAsync()` |
| Public properties | PascalCase | `DisplayName` |
| Private fields | _camelCase | `_embeddingService` |
| Local variables | camelCase | `entityCount` |
| Parameters | camelCase | `inputText` |
| Constants | PascalCase | `MaxRetries` |
| Files | PascalCase.cs | `KnowledgeGraph.cs` |
| Test files | PascalCaseTests.cs | `KnowledgeGraphTests.cs` |
| Test methods | Method_Scenario_Expected | `GetEntity_NotFound_ReturnsNull` |

### IDisposable
```csharp
// GOOD: Using statement for disposable resources
await using var connection = new SqliteConnection(_connectionString);
await connection.OpenAsync(ct);

// GOOD: Using declaration (C# 8+)
using var reader = await command.ExecuteReaderAsync(ct);

// BAD: Not disposing resources
var connection = new SqliteConnection(_connectionString); // Leaked!
```

### Static HttpClient
```csharp
// GOOD: Single static HttpClient instance per service
public class OllamaEmbeddingService : IEmbeddingService
{
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };
}

// BAD: Creating HttpClient per request — socket exhaustion
public async Task<string> CallApiAsync()
{
    using var http = new HttpClient(); // NEVER — causes socket exhaustion
    return await http.GetStringAsync(url);
}
```

---

## 2. Class Patterns

### Service Template
```csharp
public class MyService : IMyService
{
    private readonly NexusConfig _config;
    private readonly ILogger<MyService>? _logger;

    public MyService(NexusConfig config, ILogger<MyService>? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
    }

    public async Task<Result> DoWorkAsync(string input, CancellationToken ct = default)
    {
        try
        {
            // Business logic
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Service unavailable at {_config.Endpoint}. Verify the service is running.", ex);
        }
    }
}
```

### Model Template (POCO)
```csharp
public class Entity
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Summary { get; set; } = "";
    public double RelevanceScore { get; set; } = 1.0;
    public float[]? Embedding { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastMentioned { get; set; } = DateTime.UtcNow;
}
```

### ViewModel Template (Avalonia MVVM)
```csharp
public partial class MyViewModel : ObservableObject
{
    private readonly IMyService _service;

    public MyViewModel(IMyService service)
    {
        _service = service;
    }

    [ObservableProperty]
    private string _inputText = "";

    [ObservableProperty]
    private bool _isLoading;

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            var data = await _service.GetDataAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }
}
```

---

## 3. Error Handling

```csharp
// GOOD: Descriptive error messages with fix instructions
catch (HttpRequestException ex)
{
    throw new InvalidOperationException(
        $"Ollama not available at {_config.Ollama.Endpoint}. " +
        "Verify Ollama is running: ollama serve", ex);
}

// GOOD: Try/catch at service boundaries
public async Task<string> ChatAsync(string userMessage)
{
    try
    {
        var context = await _memory.BuildContextAsync(userMessage);
        var response = await _llm.GenerateAsync(prompt);
        return response;
    }
    catch (HttpRequestException ex)
    {
        return $"Error connecting to LLM: {ex.Message}. Check your configuration.";
    }
}

// BAD: Generic error messages
catch (Exception ex)
{
    throw new Exception("Something went wrong"); // Useless
}

// BAD: Swallowing exceptions silently
catch { } // NEVER
```

---

## 4. File Organization

```
src/Nexus.[Layer]/
├── I[ServiceName].cs        — Interface
├── [ServiceName].cs         — Service implementation
├── Models/
│   └── [ModelName].cs       — Data models (POCOs)
├── Config/
│   └── [ConfigSection].cs   — Configuration classes
```

**Rules:**
- One public class per file (matching filename)
- Interfaces can be in same file as implementation if small
- Group related models in `Models/` subfolder
- Keep files < 300 lines; methods < 30 lines
- No commented-out code blocks
- No `TODO`/`FIXME` in delivered code

---

## 5. Configuration

```csharp
// GOOD: All values from NexusConfig (loaded from nexus.yaml)
var endpoint = _config.Ollama.Endpoint;
var model = _config.Ollama.Model;

// BAD: Hardcoded values
var endpoint = "http://localhost:11434"; // Should be in config
var model = "qwen3:14b";                // Should be in config
```

---

## 6. DI Registration

```csharp
// GOOD: All services registered in ServiceCollectionExtensions.cs
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNexusServices(this IServiceCollection services, NexusConfig config)
    {
        services.AddSingleton(config);
        services.AddSingleton<IEmbeddingService, OllamaEmbeddingService>();
        services.AddSingleton<IKnowledgeGraph, KnowledgeGraph>();
        return services;
    }
}

// BAD: Creating services manually
var service = new OllamaEmbeddingService(config); // Should use DI
```
