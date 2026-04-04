using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Nexus.Core.Config;
using Nexus.Memory.Infrastructure;
using Spectre.Console;

namespace Nexus.CLI;

public static class OnboardingWizard
{
    public static async Task<NexusConfig> RunAsync(HttpClient? httpClient = null)
    {
        if (Console.IsInputRedirected)
            return new NexusConfig();

        try
        {
            AnsiConsole.Write(new FigletText("Nexus Agent").Color(Color.Blue));
            AnsiConsole.MarkupLine("[bold]Welcome to Nexus Agent![/] Let's get you set up.");
            AnsiConsole.WriteLine();

            // Step 1: Detect Ollama
            AnsiConsole.MarkupLine("\n[bold blue]Step 1/7:[/] Detecting Ollama...");
            using var localHttp = httpClient is null ? new HttpClient { Timeout = TimeSpan.FromSeconds(10) } : null;
            var http = httpClient ?? localHttp!;
            var availableModels = await DetectOllamaAsync(http);

            // Step 2: Check chat model
            AnsiConsole.MarkupLine("\n[bold blue]Step 2/7:[/] Chat model...");
            var chatModel = RecommendChatModel();
            if (availableModels.Count > 0)
            {
                await CheckModelAsync(availableModels, chatModel, "chat");
            }

            // Step 3: Check embedding model
            AnsiConsole.MarkupLine("\n[bold blue]Step 3/7:[/] Embedding model...");
            var embedModel = RecommendEmbeddingModel();
            if (availableModels.Count > 0)
            {
                await CheckModelAsync(availableModels, embedModel, "embedding");
            }

            // Step 4: Collect optional API keys
            AnsiConsole.MarkupLine("\n[bold blue]Step 4/7:[/] Cloud API Keys [dim](optional — press Enter to skip)[/]");
            var (geminiKey, anthropicKey, openaiKey) = CollectApiKeys();

            // Step 5: Optional filesystem MCP server
            AnsiConsole.MarkupLine("\n[bold blue]Step 5/7:[/] Filesystem MCP Server [dim](optional)[/]");
            McpServerEntry? mcpServer = null;
            if (AnsiConsole.Confirm("Add a filesystem MCP server?", defaultValue: false))
            {
                var directory = AnsiConsole.Prompt(
                    new TextPrompt<string>("  Directory to expose:")
                        .DefaultValue(Directory.GetCurrentDirectory()));
                mcpServer = new McpServerEntry
                {
                    Name = "filesystem",
                    Transport = "stdio",
                    Command = "npx",
                    Args = new List<string> { "-y", "@modelcontextprotocol/server-filesystem", directory }
                };
                AnsiConsole.MarkupLine($"[green]  Filesystem MCP configured for {Markup.Escape(directory)}[/]");
            }

            // Step 6: Generate config
            AnsiConsole.MarkupLine("\n[bold blue]Step 6/7:[/] Generating configuration...");
            var config = GenerateConfig(chatModel, embedModel, geminiKey, anthropicKey, openaiKey, mcpServer);

            // Step 7: Save config + create database
            AnsiConsole.MarkupLine("\n[bold blue]Step 7/7:[/] Saving configuration...");
            var configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nexus", "nexus.yaml");

            var shouldSave = true;
            if (File.Exists(configPath))
            {
                shouldSave = AnsiConsole.Confirm(
                    $"Config already exists at [bold]{Markup.Escape(configPath)}[/]. Overwrite?",
                    defaultValue: false);
            }

            if (shouldSave)
            {
                try
                {
                    ConfigLoader.Save(config, configPath);
                    AnsiConsole.MarkupLine($"[green]Configuration saved to {Markup.Escape(configPath)}[/]");
                }
                catch (IOException ex)
                {
                    AnsiConsole.MarkupLine($"[yellow]Warning: Could not save config: {Markup.Escape(ex.Message)}[/]");
                }
                catch (UnauthorizedAccessException ex)
                {
                    AnsiConsole.MarkupLine($"[yellow]Warning: Could not save config: {Markup.Escape(ex.Message)}[/]");
                }
            }
            else
            {
                AnsiConsole.MarkupLine("[yellow]Configuration save skipped. Continuing with database initialization...[/]");
            }

            var dbPath = ConfigLoader.GetDatabasePath(config);
            try
            {
                var dbInit = new DatabaseInitializer(dbPath);
                dbInit.Initialize();
                AnsiConsole.MarkupLine($"[green]Database created at {Markup.Escape(dbPath)}[/]");
            }
            catch (SqliteException ex)
            {
                AnsiConsole.MarkupLine($"[yellow]Warning: Could not initialize database: {Markup.Escape(ex.Message)}[/]");
            }
            catch (IOException ex)
            {
                AnsiConsole.MarkupLine($"[yellow]Warning: Could not initialize database: {Markup.Escape(ex.Message)}[/]");
            }
            catch (UnauthorizedAccessException ex)
            {
                AnsiConsole.MarkupLine($"[yellow]Warning: Could not initialize database: {Markup.Escape(ex.Message)}[/]");
            }

            // Summary
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold green]Setup complete![/]");
            AnsiConsole.MarkupLine($"  Config: [bold]{Markup.Escape(configPath)}[/]");
            AnsiConsole.MarkupLine($"  Database: [bold]{Markup.Escape(dbPath)}[/]");
            AnsiConsole.MarkupLine($"  Chat model: [bold]{Markup.Escape(chatModel)}[/]");
            AnsiConsole.MarkupLine($"  Embedding model: [bold]{Markup.Escape(embedModel)}[/]");

            var cloudProviders = new List<string>();
            if (!string.IsNullOrEmpty(geminiKey)) cloudProviders.Add("Gemini");
            if (!string.IsNullOrEmpty(anthropicKey)) cloudProviders.Add("Anthropic");
            if (!string.IsNullOrEmpty(openaiKey)) cloudProviders.Add("OpenAI");
            if (cloudProviders.Count > 0)
                AnsiConsole.MarkupLine($"  Cloud providers: [bold]{string.Join(", ", cloudProviders)}[/]");

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Run [bold]nexus chat[/] to start chatting.[/]");

            return config;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error during setup: {Markup.Escape(ex.Message)}[/]");
            AnsiConsole.MarkupLine("[dim]Run [bold]nexus init[/] to retry.[/]");
            return new NexusConfig();
        }
    }

    internal static async Task<List<string>> ParseOllamaTagsAsync(HttpClient http)
    {
        var response = await http.GetAsync("http://localhost:11434/api/tags");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<OllamaTagsResponse>();
        return json?.Models?.Select(m => m.Name ?? "").Where(n => n.Length > 0).ToList()
               ?? new List<string>();
    }

    private static async Task<List<string>> DetectOllamaAsync(HttpClient http)
    {
        try
        {
            var models = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Detecting Ollama...", async _ =>
                {
                    return await ParseOllamaTagsAsync(http);
                });

            if (models.Count > 0)
            {
                AnsiConsole.MarkupLine($"[green]Ollama detected with {models.Count} model(s) available.[/]");
            }

            return models;
        }
        catch (HttpRequestException)
        {
            AnsiConsole.MarkupLine("[yellow]Ollama not detected.[/]");
            AnsiConsole.MarkupLine("[dim]Install Ollama from https://ollama.ai to use local models.[/]");
            AnsiConsole.MarkupLine("[dim]You can still use Nexus Agent with cloud providers (configure API keys in the next step).[/]");
            return new List<string>();
        }
        catch (TaskCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]Ollama connection timed out.[/]");
            AnsiConsole.MarkupLine("[dim]Install Ollama from https://ollama.ai to use local models.[/]");
            AnsiConsole.MarkupLine("[dim]You can still use Nexus Agent with cloud providers (configure API keys in the next step).[/]");
            return new List<string>();
        }
    }

    private static async Task CheckModelAsync(List<string> availableModels, string modelName, string modelPurpose)
    {
        if (availableModels.Any(m =>
                m.Equals(modelName, StringComparison.OrdinalIgnoreCase)
                || m.Equals($"{modelName}:latest", StringComparison.OrdinalIgnoreCase)
                || m.StartsWith($"{modelName}:", StringComparison.OrdinalIgnoreCase)))
        {
            AnsiConsole.MarkupLine($"[green]  {Markup.Escape(modelName)} ({modelPurpose}) is available.[/]");
            return;
        }

        AnsiConsole.MarkupLine($"[yellow]  {Markup.Escape(modelName)} ({modelPurpose}) is not installed.[/]");

        if (AnsiConsole.Confirm($"Pull {modelName}?", defaultValue: true))
        {
            try
            {
                AnsiConsole.MarkupLine($"  Pulling [bold]{Markup.Escape(modelName)}[/]...");
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "ollama",
                        Arguments = $"pull {modelName}",
                        UseShellExecute = false,
                        CreateNoWindow = false
                    }
                };

                process.Start();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                {
                    AnsiConsole.MarkupLine($"[green]  {Markup.Escape(modelName)} pulled successfully.[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[yellow]  Warning: Could not pull {Markup.Escape(modelName)}.[/]");
                }
            }
            catch (InvalidOperationException ex)
            {
                AnsiConsole.MarkupLine($"[yellow]  Warning: Could not pull model: {Markup.Escape(ex.Message)}[/]");
            }
            catch (Win32Exception ex)
            {
                AnsiConsole.MarkupLine($"[yellow]  Warning: 'ollama' command not found: {Markup.Escape(ex.Message)}[/]");
            }
        }
    }

    private static (string? gemini, string? anthropic, string? openai) CollectApiKeys()
    {
        var gemini = AnsiConsole.Prompt(
            new TextPrompt<string>("  [dim]Gemini API key (Enter to skip):[/]").AllowEmpty().Secret());
        var anthropic = AnsiConsole.Prompt(
            new TextPrompt<string>("  [dim]Anthropic API key (Enter to skip):[/]").AllowEmpty().Secret());
        var openai = AnsiConsole.Prompt(
            new TextPrompt<string>("  [dim]OpenAI API key (Enter to skip):[/]").AllowEmpty().Secret());

        return (
            string.IsNullOrWhiteSpace(gemini) ? null : gemini.Trim(),
            string.IsNullOrWhiteSpace(anthropic) ? null : anthropic.Trim(),
            string.IsNullOrWhiteSpace(openai) ? null : openai.Trim()
        );
    }

    internal static NexusConfig GenerateConfig(
        string chatModel, string embedModel,
        string? geminiKey, string? anthropicKey, string? openaiKey,
        McpServerEntry? mcpServer = null)
    {
        var config = new NexusConfig
        {
            Models = new ModelsConfig
            {
                Local = new ModelProviderConfig
                {
                    Provider = "ollama",
                    Model = chatModel
                }
            },
            Embeddings = new EmbeddingsConfig
            {
                Provider = "ollama",
                Model = embedModel
            }
        };

        if (!string.IsNullOrEmpty(geminiKey))
        {
            config.Models.Gemini = new ProviderKeyConfig { ApiKey = geminiKey };
        }

        if (!string.IsNullOrEmpty(anthropicKey))
        {
            config.Models.Anthropic = new ProviderKeyConfig { ApiKey = anthropicKey };
        }

        if (!string.IsNullOrEmpty(openaiKey))
        {
            config.Models.OpenAi = new ProviderKeyConfig { ApiKey = openaiKey };
        }

        if (mcpServer is not null)
        {
            config.Mcp.Servers.Add(mcpServer);
        }

        return config;
    }

    private static string RecommendChatModel() => new ModelProviderConfig().Model;

    private static string RecommendEmbeddingModel() => new EmbeddingsConfig().Model;

    private class OllamaTagsResponse
    {
        [JsonPropertyName("models")]
        public List<OllamaModelInfo>? Models { get; set; }
    }

    private class OllamaModelInfo
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
