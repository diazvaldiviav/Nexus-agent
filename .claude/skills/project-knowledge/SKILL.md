# Skill: Project Knowledge — Nexus Agent (.NET 9)

> Load this skill when working on any project task. Contains the architecture overview, file structure, technology stack, conventions, and design decisions for the Nexus Agent application.

---

## Source of Truth

All architectural decisions, flows, and design specifications are defined in these two documents:

- **`docs/nexus-agent-documento-completo.md`** — Complete technical document: vision, architecture (4 layers), technology stack, memory system design, model router, MCP connectivity, desktop UI scope, configuration, and all confirmed technical decisions.
- **`docs/architecture-diagram.md`** — Mermaid diagrams for all logical flows: ChatAsync (main loop), Startup, Semantic Search, Entity Extraction, MemoryContextBuilder, Function Calling + MCP, Decay Temporal, Graph Visualization, Settings, and how they connect.

**When in doubt, these documents are authoritative.** Sprint plans, requirements, and architecture designs must align with them — not the other way around.

---

## What is Nexus Agent?

A **personal AI agent** built in C# (.NET 9) with:
- **Persistent knowledge graph memory** — remembers everything across conversations
- **LLM orchestration** — local (Ollama) and cloud (Anthropic/OpenAI/Google) providers
- **MCP connectivity** — connects to external tools via Model Context Protocol
- **Desktop UI** — Avalonia cross-platform app with chat, graph visualization, settings
- **CLI** — Spectre.Console terminal interface

**Core philosophy:** "La memoria ES el producto." All design decisions prioritize the knowledge graph.

---

## Technology Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10, C# 13 |
| Orchestration | AgentService + ModelRouter + PromptBuilder + MemoryContextBuilder |
| LLM Local | Ollama (qwen3:14b) via HTTP API |
| LLM Cloud | Anthropic / OpenAI / Google via HTTP API |
| Embeddings Local | Ollama + nomic-embed-text (768d) |
| Embeddings Cloud | OpenAI text-embedding-3-small |
| Database | SQLite (Microsoft.Data.Sqlite) |
| Vector Search | Cosine similarity in-process |
| MCP Client | ModelContextProtocol NuGet SDK |
| Desktop UI | Avalonia UI 11.x (MVVM, CommunityToolkit.Mvvm) |
| CLI | Spectre.Console |
| Config | YAML (YamlDotNet) |
| DI | Microsoft.Extensions.DependencyInjection |
| Testing | xUnit + Moq / NSubstitute |

---

## Architecture — 4 Layers

```
CAPA 0: INTERFAZ        → Desktop (Avalonia) + CLI (Spectre.Console)
CAPA 1: ORQUESTACION    → AgentService + ModelRouter + PromptBuilder
CAPA 2: MOTOR DE MEMORIA → KnowledgeGraph + SemanticSearch + EntityExtractor + RelevanceDecay
CAPA 3: CONECTIVIDAD    → McpClientManager + ToolRegistry
```

**Dependency flow:** Interface → Core → Memory + Connectors. **Never the reverse.**

---

## Project Structure

```
nexus-agent/
├── src/
│   ├── Nexus.Memory/             # Knowledge graph, embeddings, semantic search, decay
│   │   ├── Models/               # Entity, Relation, Interaction POCOs
│   │   ├── IEmbeddingService.cs  # Interface for text embedding generation
│   │   ├── ILlmClient.cs        # Interface for LLM text generation (cross-layer)
│   │   ├── EmbeddingOptions.cs   # Config record (Endpoint, Model, Dimensions)
│   │   ├── OllamaEmbeddingService.cs # Ollama HTTP API embedding implementation
│   │   ├── OpenAiEmbeddingService.cs # OpenAI HTTP API embedding implementation
│   │   ├── KnowledgeGraph.cs     # SQLite CRUD for entities/relations/interactions
│   │   ├── SemanticSearch.cs     # Embedding-based similarity search
│   │   ├── EntityExtractor.cs   # LLM-based entity extraction with 3-level fallback + auto-embedding
│   │   ├── MemoryContextBuilder.cs # Build 3-level memory context with semantic search
│   │   ├── RelevanceDecay.cs    # Time-based relevance decay
│   │   └── DatabaseInitializer.cs # SQLite schema setup
│   │
│   ├── Nexus.Core/              # Agent orchestration, model routing, prompts
│   │   ├── Config/
│   │   │   ├── ConfigLoader.cs  # YAML config loading
│   │   │   └── NexusConfig.cs   # Configuration model
│   │   ├── AgentService.cs      # Main agent loop
│   │   ├── ModelRouter.cs       # Select local vs cloud LLM
│   │   ├── OllamaLlmClient.cs  # ILlmClient implementation via Ollama HTTP API
│   │   ├── PromptBuilder.cs     # Build LLM prompts with memory context
│   │   └── ServiceCollectionExtensions.cs # DI registration
│   │
│   ├── Nexus.Connectors/        # External tool connectivity
│   │   ├── McpClientManager.cs  # MCP protocol client
│   │   └── ToolRegistry.cs      # Available tools registry
│   │
│   ├── Nexus.Desktop/           # Avalonia UI (MVVM)
│   │   ├── Views/               # AXAML views
│   │   │   ├── ChatView.axaml
│   │   │   ├── MemoryGraphView.axaml
│   │   │   ├── SettingsView.axaml
│   │   │   └── ActionLogView.axaml
│   │   ├── ViewModels/          # MVVM ViewModels
│   │   │   ├── ChatViewModel.cs
│   │   │   ├── MemoryGraphViewModel.cs
│   │   │   ├── SettingsViewModel.cs
│   │   │   └── ActionLogViewModel.cs
│   │   └── Controls/
│   │       └── GraphCanvas.cs   # Custom graph rendering control
│   │
│   └── Nexus.CLI/               # Terminal interface
│       └── Program.cs           # Spectre.Console chat loop
│
├── tests/
│   ├── Nexus.Memory.Tests/      # Memory layer tests
│   ├── Nexus.Core.Tests/        # Core orchestration tests
│   └── Nexus.Integration.Tests/ # End-to-end tests
│
├── docs/                        # Documentation
│   ├── user-requirements.md
│   ├── sprint-1.md
│   └── nexus-agent-documento-completo.md
│
├── nexus.yaml.example           # Example configuration
└── NexusAgent.slnx              # Solution file
```

---

## Design Principles

1. **La memoria ES el producto.** All design decisions prioritize the knowledge graph
2. **Usar, no construir.** Leverage existing frameworks and NuGet packages
3. **Local-first.** Must work 100% offline with Ollama. Cloud is optional
4. **Every LLM component allows local OR cloud.** Always provide both paths
5. **Mantenible por una persona.** No microservices, no over-engineering
6. **Interface segregation.** Define interfaces (IEmbeddingService, ILlmProvider) for swappable implementations

---

## Key Conventions

### DI Registration
All services registered in `src/Nexus.Core/ServiceCollectionExtensions.cs`:
```csharp
// IEmbeddingService uses DI factory for provider selection (ollama | openai)
services.AddSingleton<IEmbeddingService>(sp => config.Embeddings.Provider == "openai"
    ? new OpenAiEmbeddingService(options, apiKey)
    : new OllamaEmbeddingService(options));
services.AddSingleton<ILlmClient, OllamaLlmClient>(); // Cross-layer: interface in Memory, impl in Core
services.AddSingleton<IKnowledgeGraph, KnowledgeGraph>();
```

### Configuration Model
`src/Nexus.Core/Config/NexusConfig.cs` — loaded from `nexus.yaml` via YamlDotNet:
```csharp
public class NexusConfig
{
    public OllamaConfig Ollama { get; set; } = new();
    public EmbeddingsConfig Embeddings { get; set; } = new();
    public MemoryConfig Memory { get; set; } = new();
    public McpConfig Mcp { get; set; } = new();
}
```

### Database
SQLite with schema managed by `DatabaseInitializer.cs`. Tables: `entities`, `relations`, `interactions`, `agent_actions`.

### Testing
- xUnit for all tests
- Moq / NSubstitute for mocking interfaces
- In-memory SQLite for database tests
- Mock HttpMessageHandler for HTTP tests

### Build & Test Commands
```bash
dotnet build                                          # Build all
dotnet test                                           # Run all tests
dotnet test tests/Nexus.Memory.Tests/                 # Run specific project
dotnet test --filter "FullyQualifiedName~ClassName"   # Run specific class
dotnet run --project src/Nexus.CLI -- chat            # Run CLI
```

---

## Codebase Scan Patterns

When searching the existing codebase:
```
Glob: src/Nexus.Memory/**/*.cs      — All memory layer files
Glob: src/Nexus.Core/**/*.cs        — All core layer files
Glob: src/Nexus.Connectors/**/*.cs  — All connector files
Glob: src/Nexus.Desktop/**/*.cs     — All desktop UI files
Glob: src/Nexus.CLI/**/*.cs         — CLI files
Grep: "interface I"                 — Find existing interfaces
Grep: "class.*Service"              — Find existing services
Grep: "TODO|HACK|STUB"             — Find incomplete work
```
