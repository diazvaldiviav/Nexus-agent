# Nexus Agent

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-9.0-purple)](https://dotnet.microsoft.com)

> A personal AI agent that **really knows you** — with a persistent knowledge graph that grows with every conversation.

## What is Nexus Agent?

Nexus Agent is an open-source personal AI assistant that maintains a **persistent memory** using a knowledge graph. Unlike standard chatbots that forget everything between sessions, Nexus remembers your projects, people, decisions, and preferences — and lets you visualize them as an interactive graph.

**Core differentiator:** After 20 conversations, Nexus has a complete map of your projects, decisions, people, and relationships — visible as an interactive knowledge graph in the desktop app.

## Features

- 🧠 **Persistent Knowledge Graph** — Entities (people, projects, tech, decisions) and their relationships stored in SQLite
- 🔍 **Semantic Memory Search** — Finds relevant context using cosine similarity on embeddings
- 📉 **Relevance Decay** — Less-mentioned entities naturally fade from active memory (exponential decay)
- 🖥️ **Desktop UI** — Avalonia cross-platform app with Chat, Memory Graph, Settings, and Action Log views
- 💻 **CLI Interface** — Full-featured command-line interface using Spectre.Console
- 🔌 **MCP Connectivity** — Connect to filesystem, git, and other MCP servers
- 🏠 **Local-first** — Works 100% offline with Ollama; cloud (Anthropic/OpenAI) is optional
- 🔀 **Model Router** — Automatically routes tasks to local or cloud models based on task complexity

## Architecture

```
┌─────────────────────────────────────────┐
│          LAYER 0: INTERFACE              │
│   Desktop App (Avalonia)  │  CLI         │
└──────────────┬──────────────────────────┘
               │
┌──────────────▼──────────────────────────┐
│        LAYER 1: ORCHESTRATION            │
│   AgentService │ ModelRouter │ Prompts   │
└──────────────┬──────────────────────────┘
               │
┌──────────────▼──────────────────────────┐
│      LAYER 2: MEMORY ENGINE ★            │
│  KnowledgeGraph │ SemanticSearch         │
│  EntityExtractor │ RelevanceDecay        │
│  MemoryContextBuilder                    │
└──────────────┬──────────────────────────┘
               │
┌──────────────▼──────────────────────────┐
│      LAYER 3: MCP CONNECTIVITY           │
│  McpClientManager │ ToolRegistry         │
└─────────────────────────────────────────┘
```

## Getting Started

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- [Ollama](https://ollama.ai) with models:
  - `ollama pull qwen3:14b` (or `qwen3:8b` for 8GB RAM)
  - `ollama pull nomic-embed-text`
- (Optional) API key for Anthropic/OpenAI/Google for cloud model routing

### Installation

```bash
git clone https://github.com/diazvaldiviav/Nexus-agent.git
cd Nexus-agent
dotnet restore
dotnet build
```

### Configuration

```bash
cp nexus.yaml.example ~/.nexus/nexus.yaml
# Edit ~/.nexus/nexus.yaml with your settings
```

### Running the CLI

```bash
# Start interactive chat
dotnet run --project src/Nexus.CLI

# Or use specific commands
dotnet run --project src/Nexus.CLI -- chat
dotnet run --project src/Nexus.CLI -- memory list
dotnet run --project src/Nexus.CLI -- memory stats
dotnet run --project src/Nexus.CLI -- connect filesystem http://localhost:3000
dotnet run --project src/Nexus.CLI -- version
```

### Running the Desktop App

```bash
dotnet run --project src/Nexus.Desktop
```

## Project Structure

```
nexus-agent/
├── src/
│   ├── Nexus.Core/           # Agent orchestration, model routing, prompts
│   ├── Nexus.Memory/         # ★ Knowledge graph, semantic search, decay
│   ├── Nexus.Connectors/     # MCP client manager and tool registry
│   ├── Nexus.Desktop/        # Avalonia desktop app (4 views)
│   └── Nexus.CLI/            # Spectre.Console CLI
└── tests/
    ├── Nexus.Memory.Tests/   # Unit tests for memory system
    ├── Nexus.Core.Tests/     # Unit tests for core services
    └── Nexus.Integration.Tests/ # Integration tests
```

## Running Tests

```bash
dotnet test
```

## Technology Stack

| Component | Technology |
|-----------|------------|
| Runtime | .NET 9 |
| LLM (local) | Ollama + Qwen 3:14b |
| LLM (cloud) | Anthropic / OpenAI / Google |
| Database | SQLite (Microsoft.Data.Sqlite) |
| Desktop UI | Avalonia UI 11.x |
| MVVM | CommunityToolkit.Mvvm |
| CLI | Spectre.Console |
| Config | YamlDotNet |

## Memory System

Nexus uses a 3-level memory architecture:

1. **Working Memory** (always in prompt) — High-relevance entities (score > 0.7, mentions > 3)
2. **Relevant Memory** (retrieved per query) — Semantic search top-K results
3. **Archive Memory** (explicit search only) — Entities with score < 0.05

Relevance uses exponential decay: `score = base × e^(-λ × days_since_mention)` where λ defaults to 0.05, with frequency boost: `λ_eff = 0.05 / log2(mentions + 1)`.

## License

MIT License — see [LICENSE](LICENSE) for details.
