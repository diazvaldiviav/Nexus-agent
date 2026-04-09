# Skill: Design Patterns — Nexus Agent (.NET 10)

> Reusable architectural patterns specific to the Nexus Agent application. Consult before any architectural decision.

---

## Pattern 1: Interface + Implementation (Primary Pattern)

**When:** Creating any service. ALL services must follow this pattern.

```csharp
// 1. Interface — defines the contract
public interface IEmbeddingService
{
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default);
}

// 2. Implementation — one or more per interface
public class OllamaEmbeddingService : IEmbeddingService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly NexusConfig _config;

    public OllamaEmbeddingService(NexusConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var payload = new { model = _config.Embeddings.Model, prompt = text };
        var response = await _http.PostAsJsonAsync(
            $"{_config.Embeddings.Endpoint}/api/embeddings", payload, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(ct);
        return result?.Embedding ?? Array.Empty<float>();
    }
}

// 3. DI Registration — wire up in ServiceCollectionExtensions.cs
services.AddSingleton<IEmbeddingService, OllamaEmbeddingService>();
```

**Decision Tree:**
```
New functionality needed?
├── Does an existing interface cover it?
│   ├── YES → Add new implementation of that interface
│   └── NO → Create new interface + implementation
├── Does an existing class already handle part of it?
│   ├── YES → Extend existing class (if SRP allows)
│   └── NO → Create new service
└── Multiple implementations needed? (local vs cloud)
    ├── YES → Strategy via DI (see Pattern 2)
    └── NO → Single implementation
```

---

## Pattern 2: Strategy via DI (Provider Selection)

**When:** A service needs local AND cloud implementations (LLM, embeddings).

```csharp
// Interface
public interface ILlmProvider
{
    Task<string> GenerateAsync(string prompt, CancellationToken ct = default);
}

// Local implementation
public class OllamaLlmProvider : ILlmProvider
{
    public async Task<string> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        // Call Ollama HTTP API
    }
}

// Cloud implementation
public class AnthropicLlmProvider : ILlmProvider
{
    public async Task<string> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        // Call Anthropic API
    }
}

// DI factory — select based on config
services.AddSingleton<ILlmProvider>(sp =>
{
    var config = sp.GetRequiredService<NexusConfig>();
    return config.Llm.Provider switch
    {
        "anthropic" => new AnthropicLlmProvider(config),
        "openai" => new OpenAiLlmProvider(config),
        _ => new OllamaLlmProvider(config), // Default to local
    };
});
```

---

## Pattern 3: MVVM (Avalonia Desktop UI)

**When:** Creating any Desktop UI view.

```csharp
// ViewModel — uses CommunityToolkit.Mvvm source generators
public partial class ChatViewModel : ObservableObject
{
    private readonly AgentService _agent;

    public ChatViewModel(AgentService agent)
    {
        _agent = agent;
    }

    [ObservableProperty]
    private string _inputText = "";

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private string _currentModel = "";

    public ObservableCollection<ChatMessage> Messages { get; } = new();

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText)) return;

        var userMessage = InputText;
        Messages.Add(new ChatMessage("user", userMessage));
        InputText = "";
        IsProcessing = true;

        try
        {
            var response = await _agent.ChatAsync(userMessage);
            Messages.Add(new ChatMessage("assistant", response.Text));
            CurrentModel = response.ModelUsed;
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatMessage("error", $"Error: {ex.Message}"));
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private bool CanSend => !IsProcessing && !string.IsNullOrWhiteSpace(InputText);
}
```

```xml
<!-- View (AXAML) — binds to ViewModel -->
<UserControl x:Class="Nexus.Desktop.Views.ChatView"
             xmlns:vm="clr-namespace:Nexus.Desktop.ViewModels">
    <Design.DataContext>
        <vm:ChatViewModel />
    </Design.DataContext>

    <DockPanel>
        <DockPanel DockPanel.Dock="Bottom">
            <Button Command="{Binding SendCommand}" DockPanel.Dock="Right" Content="Send" />
            <TextBox Text="{Binding InputText}" />
        </DockPanel>
        <ItemsControl ItemsSource="{Binding Messages}" />
    </DockPanel>
</UserControl>
```

---

## Pattern 4: Async Pipeline (Agent Loop)

**When:** Processing a user message through the full agent flow.

```
User Input
  │
  ▼
PromptBuilder (build context from memory)
  │
  ▼
ModelRouter (select local or cloud LLM)
  │
  ▼
LLM Provider (generate response)
  │
  ▼
EntityExtractor (extract entities from response)
  │
  ▼
KnowledgeGraph (persist entities + relations)
  │
  ▼
Response to User
```

```csharp
public class AgentService
{
    private readonly IKnowledgeGraph _memory;
    private readonly ModelRouter _router;
    private readonly PromptBuilder _promptBuilder;
    private readonly EntityExtractor _extractor;

    public async Task<AgentResponse> ChatAsync(string userMessage, CancellationToken ct = default)
    {
        // 1. Build context from memory
        var context = await _promptBuilder.BuildContextAsync(userMessage, ct);

        // 2. Route to appropriate LLM
        var llm = _router.SelectProvider(userMessage);

        // 3. Generate response
        var prompt = _promptBuilder.BuildPrompt(userMessage, context);
        var response = await llm.GenerateAsync(prompt, ct);

        // 4. Extract and persist entities (fire-and-forget with error handling)
        _ = Task.Run(async () =>
        {
            try
            {
                var entities = await _extractor.ExtractAsync(userMessage, response, ct);
                await _memory.PersistEntitiesAsync(entities, ct);
            }
            catch (Exception ex)
            {
                // Log but don't fail the response
            }
        }, ct);

        return new AgentResponse(response, llm.ModelName);
    }
}
```

---

## Pattern 5: Fallback Chain (Resilience)

**When:** A LLM-dependent operation might fail (extraction, summarization).

```csharp
public class EntityExtractor
{
    private readonly ILlmProvider? _llm;

    public async Task<List<Entity>> ExtractAsync(string userMessage, string response, CancellationToken ct = default)
    {
        // Try LLM-based extraction first
        if (_llm != null)
        {
            try
            {
                return await ExtractWithLlmAsync(userMessage, response, ct);
            }
            catch (Exception)
            {
                // Fall through to heuristic
            }
        }

        // Fallback: heuristic extraction (regex, keyword matching)
        return ExtractWithHeuristics(userMessage, response);
    }
}
```

---

## Pattern 6: 3-Level Memory Context

**When:** Building context for the LLM prompt from the knowledge graph.

```
Level 1: WORKING MEMORY   — Current conversation entities (last N interactions)
Level 2: RELEVANT MEMORY  — Semantically similar entities (embedding search)
Level 3: ARCHIVE MEMORY   — Decayed low-relevance entities (available but not loaded by default)
```

```csharp
public class MemoryContextBuilder
{
    public async Task<MemoryContext> BuildAsync(string query, CancellationToken ct = default)
    {
        // Level 1: Recent interactions
        var working = await _graph.GetRecentEntitiesAsync(limit: 10, ct);

        // Level 2: Semantic search
        var embedding = await _embeddings.GenerateEmbeddingAsync(query, ct);
        var relevant = await _search.FindSimilarAsync(embedding, topK: 5, ct);

        // Level 3: Archive (only if explicitly needed)
        // Retrieved on demand, not preloaded

        return new MemoryContext(working, relevant);
    }
}
```

---

## Pattern 7: Repository via KnowledgeGraph (Data Access)

**When:** All database access to SQLite.

```csharp
// All SQLite access goes through KnowledgeGraph — no direct SQL elsewhere
public class KnowledgeGraph : IKnowledgeGraph
{
    private readonly string _connectionString;

    public async Task<Entity?> GetEntityAsync(string name, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM entities WHERE name = @name";
        command.Parameters.AddWithValue("@name", name);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return MapEntity(reader);
        }
        return null;
    }
}
```

**Rules:**
- Only `KnowledgeGraph` touches SQLite directly
- Use parameterized queries — NEVER string concatenation for SQL
- Always use `async` database methods
- Always dispose connections with `await using`

---

## Pattern 8: Configuration Cascade

**When:** Loading configuration from multiple sources.

```
Priority (highest to lowest):
1. Environment variables
2. nexus.yaml (user config)
3. Default values in NexusConfig
```

```csharp
public class ConfigLoader
{
    public static NexusConfig Load(string? path = null)
    {
        path ??= FindConfigFile();
        if (path != null && File.Exists(path))
        {
            var yaml = File.ReadAllText(path);
            var deserializer = new DeserializerBuilder().Build();
            return deserializer.Deserialize<NexusConfig>(yaml) ?? new NexusConfig();
        }
        return new NexusConfig(); // Defaults
    }
}
```
