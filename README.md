# Nexus Agent

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-blue)](https://dotnet.microsoft.com)

> A personal AI agent that **really knows you** — with a persistent knowledge graph that grows with every conversation.

## Table of Contents

- [Overview](#overview)
- [Features](#features)
- [Architecture](#architecture)
- [Tech Stack](#tech-stack)
- [Prerequisites](#prerequisites)
- [Installation](#installation)
- [Configuration](#configuration)
  - [Environment Variables](#environment-variables)
  - [Configuration File](#configuration-file)
- [Running Locally](#running-locally)
  - [First-Time Setup (Onboarding Wizard)](#first-time-setup-onboarding-wizard)
  - [CLI](#cli)
  - [Desktop App](#desktop-app)
- [Running Tests](#running-tests)
- [Project Structure](#project-structure)
- [Deployment](#deployment)
- [Troubleshooting](#troubleshooting)
- [License](#license)

---

## Overview

Nexus Agent is an open-source personal AI assistant that maintains a **persistent memory** using a knowledge graph. Unlike standard chatbots that forget everything between sessions, Nexus remembers your projects, people, decisions, and preferences — and lets you visualize them as an interactive graph.

**Core differentiator:** After 20 conversations, Nexus has built a complete map of your projects, decisions, people, and relationships — all visible as an interactive knowledge graph inside the desktop app.

Nexus is **local-first**: it runs entirely offline using [Ollama](https://ollama.ai). Cloud providers (Anthropic, OpenAI, Google Gemini) are supported optionally for more capable models on complex tasks.

---

## Features

| Feature | Description |
|---|---|
| 🧠 **Persistent Knowledge Graph** | Entities (people, projects, tech, decisions) and their relationships stored in SQLite, surviving across all sessions |
| 🔍 **Semantic Memory Search** | Finds the most relevant context for each message using cosine similarity on local or cloud embeddings |
| 📉 **Relevance Decay** | Less-mentioned entities naturally fade from active memory using exponential decay, keeping context focused |
| 🗜️ **Memory Compression** | Stale archive-level entities are compressed and written to disk to keep the database lean |
| 🖥️ **Desktop UI** | Avalonia cross-platform app with Chat, Memory Graph, Settings, and Action Log views |
| 💻 **CLI Interface** | Full-featured terminal interface powered by Spectre.Console with rich output |
| 🔌 **MCP Connectivity** | Connect to filesystem, git, and any MCP-compatible server via stdio or SSE transports |
| 🏠 **Local-first** | Works 100% offline with Ollama; no data leaves your machine unless you configure a cloud provider |
| 🔀 **Model Router** | Automatically routes tasks to local or cloud models based on task type and complexity |
| ⚙️ **Onboarding Wizard** | Interactive 7-step setup wizard that detects Ollama, pulls missing models, and generates your config |

---

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
│  MemoryContextBuilder │ Compressor       │
└──────────────┬──────────────────────────┘
               │
┌──────────────▼──────────────────────────┐
│      LAYER 3: MCP CONNECTIVITY           │
│  McpClientManager │ ToolRegistry         │
└─────────────────────────────────────────┘
```

### Memory Architecture

Nexus uses a three-level memory system:

| Level | Trigger | Description |
|---|---|---|
| **Working Memory** | Always in prompt | High-relevance entities (score > 0.7 and mentions > 3) |
| **Relevant Memory** | Retrieved per query | Semantic search top-K results injected into context |
| **Archive Memory** | Explicit search only | Entities with score < 0.05, optionally compressed to disk |

Relevance score uses exponential decay:

```
score = base × e^(−λ × days_since_mention)
```

where `λ` defaults to `0.05`, with a frequency boost:

```
λ_eff = 0.05 / log₂(mentions + 1)
```

---

## Tech Stack

| Component | Technology | Version |
|---|---|---|
| Runtime | .NET | 10.0 |
| LLM — local | Ollama + Qwen 3 | 14b / 8b |
| LLM — cloud | Anthropic / OpenAI / Google Gemini | latest |
| Embeddings | Ollama (`nomic-embed-text`) / OpenAI | — |
| Database | SQLite via `Microsoft.Data.Sqlite` | 9.x |
| Desktop UI | Avalonia UI | 11.2.x |
| MVVM | CommunityToolkit.Mvvm | 8.4.x |
| CLI | Spectre.Console | 0.49.x |
| MCP Client | ModelContextProtocol | 1.1.x |
| Config | YamlDotNet | 16.x |
| Testing | xUnit + coverlet | — |

---

## Prerequisites

### Required

- [**.NET 10 SDK**](https://dotnet.microsoft.com/download) — the runtime and build toolchain
- [**Ollama**](https://ollama.ai) — required for local model inference and embeddings

  After installing Ollama, pull the required models:

  ```bash
  # Chat model (choose based on available RAM)
  ollama pull qwen3:14b   # recommended — requires ~10 GB RAM
  ollama pull qwen3:8b    # lighter alternative — requires ~6 GB RAM

  # Embedding model
  ollama pull nomic-embed-text
  ```

### Optional

- **Anthropic API key** — enables Claude models for complex reasoning tasks
- **OpenAI API key** — enables GPT-4o models and OpenAI embeddings
- **Google Gemini API key** — enables Gemini Flash / Pro models
- **Node.js** (for filesystem MCP server via `npx`)
- **Python / `uvx`** (for the git MCP server)

---

## Installation

```bash
git clone https://github.com/diazvaldiviav/Nexus-agent.git
cd Nexus-agent
dotnet restore
dotnet build
```

> **Tip:** The first `dotnet build` may take a minute while NuGet packages are downloaded.

---

## Configuration

Nexus Agent is configured via a YAML file at `~/.nexus/nexus.yaml`. An annotated example is provided in [`nexus.yaml.example`](nexus.yaml.example).

### First Run

The easiest way to configure Nexus is through the interactive onboarding wizard (see [Running Locally](#first-time-setup-onboarding-wizard)). It auto-detects Ollama, pulls any missing models, collects optional API keys, and writes the config file for you.

For a manual setup, copy the example and edit it:

```bash
# Linux / macOS
cp nexus.yaml.example ~/.nexus/nexus.yaml

# Windows (PowerShell)
Copy-Item nexus.yaml.example "$env:USERPROFILE\.nexus\nexus.yaml"
```

### Environment Variables

API keys can be supplied via environment variables instead of (or in addition to) the config file. Environment variables take precedence when both are set.

| Variable | Provider | Description |
|---|---|---|
| `GEMINI_API_KEY` or `GOOGLE_API_KEY` | Google Gemini | API key for Gemini models |
| `ANTHROPIC_API_KEY` | Anthropic | API key for Claude models |
| `OPENAI_API_KEY` | OpenAI | API key for GPT-4o models and OpenAI embeddings |

**Example (Linux / macOS):**

```bash
export ANTHROPIC_API_KEY="sk-ant-..."
export GEMINI_API_KEY="AIza..."
export OPENAI_API_KEY="sk-..."
```

**Example (Windows PowerShell):**

```powershell
$env:ANTHROPIC_API_KEY = "sk-ant-..."
$env:GEMINI_API_KEY    = "AIza..."
$env:OPENAI_API_KEY    = "sk-..."
```

> Keys set in environment variables are never written to disk by Nexus.

### Configuration File

The key sections of `~/.nexus/nexus.yaml`:

```yaml
agent:
  name: "Nexus"
  language: "en"

models:
  local:
    provider: ollama
    model: qwen3:14b
    endpoint: http://localhost:11434
  cloud:
    provider: gemini            # gemini | anthropic | openai
    model: gemini-2.5-flash-lite

  # Per-provider API keys (can use env-var placeholders)
  gemini:
    api_key: ${GEMINI_API_KEY}
  anthropic:
    api_key: ${ANTHROPIC_API_KEY}
  openai:
    api_key: ${OPENAI_API_KEY}

  routing:
    entity_extraction: local
    interaction_summary: local
    complex_reasoning: cloud
    code_generation: cloud
    default: local

embeddings:
  provider: ollama
  model: nomic-embed-text
  endpoint: http://localhost:11434
  dimensions: 768

memory:
  database: ~/.nexus/memory.db
  working_memory_max_tokens: 1000
  relevant_memory_max_tokens: 3000
  relevance_decay_lambda: 0.05
  deduplication_threshold: 0.85
  compression_enabled: true

mcp:
  max_tool_call_iterations: 3
  tool_call_timeout_seconds: 30
  servers: []   # add MCP server entries here
```

See [`nexus.yaml.example`](nexus.yaml.example) for the full reference with all options documented.

---

## Running Locally

### First-Time Setup (Onboarding Wizard)

Run the CLI without arguments to trigger the interactive 7-step onboarding wizard:

```bash
dotnet run --project src/Nexus.CLI
```

The wizard will:

1. Detect whether Ollama is running
2. Check if the recommended chat model is installed (offers to pull it)
3. Check if the embedding model is installed (offers to pull it)
4. Optionally collect Gemini, Anthropic, and OpenAI API keys
5. Optionally configure a filesystem MCP server
6. Generate `~/.nexus/nexus.yaml`
7. Initialize the SQLite database at `~/.nexus/memory.db`

Skip the wizard at any time by copying and editing the config file manually (see [Configuration](#configuration)).

### CLI

```bash
# Start interactive chat session
dotnet run --project src/Nexus.CLI -- chat

# Memory management
dotnet run --project src/Nexus.CLI -- memory list    # list stored entities
dotnet run --project src/Nexus.CLI -- memory stats   # show memory statistics

# MCP connectivity
dotnet run --project src/Nexus.CLI -- connect filesystem http://localhost:3000

# Show version
dotnet run --project src/Nexus.CLI -- version
```

### Desktop App

```bash
dotnet run --project src/Nexus.Desktop
```

The desktop app opens with four views accessible from the sidebar:

| View | Description |
|---|---|
| **Chat** | Conversational interface with the agent |
| **Memory Graph** | Interactive force-directed graph of all stored entities and relationships |
| **Settings** | Live configuration editor (model selection, API keys, memory thresholds) |
| **Action Log** | Real-time log of tool calls, memory reads/writes, and model invocations |

---

## Running Tests

Run the full test suite:

```bash
dotnet test
```

Run a specific test project:

```bash
dotnet test tests/Nexus.Core.Tests
dotnet test tests/Nexus.Memory.Tests
dotnet test tests/Nexus.Desktop.Tests
dotnet test tests/Nexus.Integration.Tests
```

Run with coverage:

```bash
dotnet test --collect:"XPlat Code Coverage"
```

> **Note:** Integration tests (`Nexus.Integration.Tests`) exercise the full agent pipeline including config loading and DI wiring. They do not require a live Ollama instance — all LLM calls are mocked.

---

## Project Structure

```
Nexus-agent/
├── src/
│   ├── Nexus.Core/              # Agent orchestration, model routing, prompt builder
│   ├── Nexus.Memory/            # ★ Knowledge graph, semantic search, decay, compression
│   ├── Nexus.Connectors/        # MCP client manager and tool registry
│   ├── Nexus.Desktop/           # Avalonia desktop app (Chat, Graph, Settings, Log)
│   └── Nexus.CLI/               # Spectre.Console CLI + onboarding wizard
├── tests/
│   ├── Nexus.Core.Tests/        # Unit tests — LLM providers, config, model router
│   ├── Nexus.Memory.Tests/      # Unit tests — knowledge graph, embeddings, decay
│   ├── Nexus.Desktop.Tests/     # Unit tests — ViewModels, Markdown renderer
│   └── Nexus.Integration.Tests/ # Integration tests — end-to-end agent flows
├── nexus.yaml.example           # Annotated configuration reference
├── NexusAgent.slnx              # Solution file
└── LICENSE
```

---

## Deployment

Nexus Agent is a **personal desktop application** and is not designed to run as a hosted service. However, you can distribute self-contained executables to other machines.

### Building a Self-Contained Executable

**CLI:**

```bash
# Linux (x64)
dotnet publish src/Nexus.CLI -c Release -r linux-x64 --self-contained true -o publish/cli

# macOS (Apple Silicon)
dotnet publish src/Nexus.CLI -c Release -r osx-arm64 --self-contained true -o publish/cli

# Windows (x64)
dotnet publish src/Nexus.CLI -c Release -r win-x64 --self-contained true -o publish/cli
```

**Desktop App:**

```bash
# Linux (x64)
dotnet publish src/Nexus.Desktop -c Release -r linux-x64 --self-contained true -o publish/desktop

# macOS (Apple Silicon)
dotnet publish src/Nexus.Desktop -c Release -r osx-arm64 --self-contained true -o publish/desktop

# Windows (x64)
dotnet publish src/Nexus.Desktop -c Release -r win-x64 --self-contained true -o publish/desktop
```

### Data & Configuration Paths

All user data is stored under `~/.nexus/`:

| Path | Description |
|---|---|
| `~/.nexus/nexus.yaml` | Main configuration file |
| `~/.nexus/memory.db` | SQLite knowledge graph database |
| `~/.nexus/archive/` | Compressed archived entities (JSON) |

On Windows, `~` resolves to `%USERPROFILE%` (e.g. `C:\Users\<you>\.nexus\`).

### Upgrading

After pulling a new version, rebuild and re-run. The database schema is forward-compatible; no manual migration is needed for MVP releases.

```bash
git pull
dotnet build
```

---

## Troubleshooting

### Ollama is not detected

**Symptom:** `Ollama not detected` during the wizard, or errors like `Connection refused` on `http://localhost:11434`.

**Fix:**
1. Make sure Ollama is installed: [https://ollama.ai](https://ollama.ai)
2. Start the Ollama service:
   - **macOS / Linux:** `ollama serve` (or it starts automatically after install)
   - **Windows:** Ollama runs as a system tray app; check that it is running
3. Verify it responds: `curl http://localhost:11434/api/tags`

---

### Required model is not installed

**Symptom:** Errors mentioning `model not found` or the wizard asking to pull a model.

**Fix:**

```bash
ollama pull qwen3:14b        # or qwen3:8b for lower RAM
ollama pull nomic-embed-text
```

---

### Config file not found

**Symptom:** `Configuration file not found` on startup.

**Fix:** Run the onboarding wizard, or copy the example config manually:

```bash
mkdir -p ~/.nexus
cp nexus.yaml.example ~/.nexus/nexus.yaml
```

---

### API key errors (cloud providers)

**Symptom:** Errors like `401 Unauthorized` or `Invalid API key` when using cloud models.

**Fix:**
1. Verify the key is correct in `~/.nexus/nexus.yaml` or set via environment variable.
2. Make sure the `cloud` provider in your config matches the key you supplied (e.g., `provider: anthropic` needs `ANTHROPIC_API_KEY`).
3. Check that the model name matches the provider's current offerings (see comments in [`nexus.yaml.example`](nexus.yaml.example)).

---

### Desktop app window does not open

**Symptom:** `dotnet run --project src/Nexus.Desktop` exits immediately or shows no window.

**Fix:**
1. On Linux, ensure a display server is available (`echo $DISPLAY` should be set for X11, or use a Wayland session).
2. Check the terminal output for startup errors — the app logs warnings and errors to `stderr`.
3. Try running the CLI first to verify the config and database are correctly initialized.

---

### Database errors on startup

**Symptom:** `SQLite error` or `unable to open database file`.

**Fix:**
1. Ensure `~/.nexus/` exists and is writable: `mkdir -p ~/.nexus`
2. Delete a corrupted database and let Nexus recreate it: `rm ~/.nexus/memory.db`
3. Verify the `memory.database` path in `nexus.yaml` is valid.

---

### MCP tool calls fail or time out

**Symptom:** Tool calls return errors, or the agent hangs for a long time during tool use.

**Fix:**
1. Verify the MCP server process is running (for `stdio` transport, Nexus launches it automatically — check that `npx` or `uvx` is on your `PATH`).
2. Increase the timeout in `nexus.yaml`: `mcp.tool_call_timeout_seconds: 60`
3. Reduce the iteration cap if the agent loops: `mcp.max_tool_call_iterations: 2`

---

## License

MIT License — see [LICENSE](LICENSE) for details.
