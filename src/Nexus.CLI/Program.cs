using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.Core;
using Nexus.Core.Config;
using Nexus.Memory;
using Spectre.Console;

var configPath = args.Length > 0 && args[0] == "--config" && args.Length > 1 ? args[1] : null;
var config = ConfigLoader.Load(configPath);

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Error));
services.AddNexusAgent(config);
var sp = services.BuildServiceProvider();

if (args.Length == 0 || args[0] == "chat")
{
    await RunChatAsync(sp, config);
}
else if (args[0] == "memory")
{
    await RunMemoryCommandAsync(sp, args.Skip(1).ToArray());
}
else if (args[0] == "connect")
{
    await RunConnectCommandAsync(sp, args.Skip(1).ToArray());
}
else if (args[0] == "version")
{
    AnsiConsole.MarkupLine("[bold blue]Nexus Agent[/] v1.0.0-mvp");
}
else
{
    ShowHelp();
}

static async Task RunChatAsync(IServiceProvider sp, NexusConfig config)
{
    var agentService = sp.GetRequiredService<AgentService>();
    var decay = sp.GetRequiredService<RelevanceDecay>();
    
    // Apply memory decay on startup
    await decay.ApplyDecayAsync();

    AnsiConsole.Clear();
    AnsiConsole.Write(new FigletText("Nexus Agent").Color(Color.Blue));
    AnsiConsole.MarkupLine($"[dim]Model: {config.Models.Local.Provider}/{config.Models.Local.Model} (local)[/]");
    AnsiConsole.MarkupLine("[dim]Type your message, or 'exit' to quit, 'clear' to clear history[/]");
    AnsiConsole.WriteLine();

    while (true)
    {
        var userInput = AnsiConsole.Ask<string>("[bold green]You>[/]");
        
        if (string.IsNullOrWhiteSpace(userInput)) continue;
        if (userInput.ToLower() == "exit")
        {
            AnsiConsole.MarkupLine("[dim]Saving pending entities...[/]");
            await agentService.FlushPendingExtractionAsync();
            break;
        }
        if (userInput.ToLower() == "clear")
        {
            agentService.ClearHistory();
            AnsiConsole.MarkupLine("[dim]Conversation history cleared.[/]");
            continue;
        }

        try
        {
            AnsiConsole.WriteLine();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var firstToken = true;

            // Show spinner until first token arrives
            var cts = new CancellationTokenSource();
            var spinnerTask = Task.Run(async () =>
            {
                var frames = new[] { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
                var i = 0;
                while (!cts.Token.IsCancellationRequested)
                {
                    Console.Write($"\r[dim]{frames[i++ % frames.Length]} Thinking...[/]");
                    try { await Task.Delay(80, cts.Token); } catch (OperationCanceledException) { break; }
                }
            });

            await foreach (var token in agentService.ChatStreamAsync(userInput,
                onEntitiesExtracted: count =>
                {
                    AnsiConsole.MarkupLine($"[dim]  ✓ {count} entities extracted and saved to memory[/]");
                }))
            {
                if (firstToken)
                {
                    cts.Cancel();
                    await spinnerTask;
                    Console.Write("\r                    \r");
                    AnsiConsole.Markup("[bold blue]Nexus>[/] ");
                    firstToken = false;
                }
                Console.Write(token);
            }

            if (firstToken)
            {
                cts.Cancel();
                await spinnerTask;
                Console.Write("\r                    \r");
                AnsiConsole.Markup("[bold blue]Nexus>[/] ");
            }

            sw.Stop();
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[dim]({config.Models.Local.Provider}/{config.Models.Local.Model}, {sw.ElapsedMilliseconds}ms) → Extracting entities in background...[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
        }
        
        AnsiConsole.WriteLine();
    }
}

static async Task RunMemoryCommandAsync(IServiceProvider sp, string[] args)
{
    var graph = sp.GetRequiredService<KnowledgeGraph>();
    
    if (args.Length == 0 || args[0] == "list")
    {
        var entities = await graph.GetAllEntitiesAsync();
        
        if (entities.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No entities in memory yet. Start chatting to build your knowledge graph![/]");
            return;
        }
        
        var table = new Table();
        table.AddColumn("Name");
        table.AddColumn("Type");
        table.AddColumn("Score");
        table.AddColumn("Level");
        table.AddColumn("Mentions");
        table.AddColumn("Embed");
        table.AddColumn("Summary");

        foreach (var e in entities.Take(50))
        {
            var typeColor = e.Type.ToString() switch
            {
                "Person" => "blue",
                "Project" => "green",
                "Technology" => "orange1",
                "Decision" => "red",
                _ => "white"
            };
            
            var hasEmbed = e.Embedding is not null && e.Embedding.Length > 0;
            table.AddRow(
                $"[bold]{EscapeMarkup(e.Name)}[/]",
                $"[{typeColor}]{e.Type}[/]",
                $"{e.RelevanceScore:F2}",
                e.MemoryLevel.ToString(),
                e.MentionCount.ToString(),
                hasEmbed ? "[green]yes[/]" : "[red]no[/]",
                EscapeMarkup(e.TextSummary?[..Math.Min(50, e.TextSummary?.Length ?? 0)] ?? "-")
            );
        }
        
        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[dim]Showing {Math.Min(50, entities.Count)} of {entities.Count} entities[/]");
    }
    else if (args[0] == "stats")
    {
        var entities = await graph.GetAllEntitiesAsync();
        var relations = await graph.GetAllRelationsAsync();
        var actions = await graph.GetRecentActionsAsync(1000);
        
        var withEmbeddings = entities.Count(e => e.Embedding is not null && e.Embedding.Length > 0);
        AnsiConsole.MarkupLine($"[bold]Memory Statistics[/]");
        AnsiConsole.MarkupLine($"  Entities: [bold]{entities.Count}[/]");
        AnsiConsole.MarkupLine($"  With embeddings: [bold]{withEmbeddings}/{entities.Count}[/]");
        AnsiConsole.MarkupLine($"  Relations: [bold]{relations.Count}[/]");
        AnsiConsole.MarkupLine($"  Actions logged: [bold]{actions.Count}[/]");
        
        var byType = entities.GroupBy(e => e.Type).OrderByDescending(g => g.Count());
        AnsiConsole.MarkupLine("\n[bold]Entities by type:[/]");
        foreach (var group in byType)
            AnsiConsole.MarkupLine($"  {group.Key}: {group.Count()}");
            
        var byLevel = entities.GroupBy(e => e.MemoryLevel);
        AnsiConsole.MarkupLine("\n[bold]By memory level:[/]");
        foreach (var group in byLevel)
            AnsiConsole.MarkupLine($"  {group.Key}: {group.Count()}");
    }
    else
    {
        AnsiConsole.MarkupLine("Usage: nexus memory [list|stats]");
    }
    
    await Task.CompletedTask;
}

static async Task RunConnectCommandAsync(IServiceProvider sp, string[] args)
{
    if (args.Length < 2)
    {
        AnsiConsole.MarkupLine("Usage: nexus connect <name> <url>");
        return;
    }
    
    var name = args[0];
    var url = args[1];
    
    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .StartAsync($"Connecting to {name}...", async ctx =>
        {
            var manager = sp.GetRequiredService<Nexus.Connectors.McpClientManager>();
            var connected = await manager.ConnectAsync(name, url);
            
            if (connected)
                AnsiConsole.MarkupLine($"[green]✓[/] Connected to MCP server [bold]{name}[/] at {url}");
            else
                AnsiConsole.MarkupLine($"[yellow]⚠[/] Could not verify connection to [bold]{name}[/] at {url}. Server may be offline.");
        });
}

static void ShowHelp()
{
    AnsiConsole.Write(new FigletText("Nexus Agent").Color(Color.Blue));
    AnsiConsole.MarkupLine("[bold]Usage:[/] nexus [command] [options]");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[bold]Commands:[/]");
    AnsiConsole.MarkupLine("  [green]chat[/]              Start interactive chat (default)");
    AnsiConsole.MarkupLine("  [green]memory list[/]       List all entities in memory");
    AnsiConsole.MarkupLine("  [green]memory stats[/]      Show memory statistics");
    AnsiConsole.MarkupLine("  [green]connect <n> <url>[/]  Connect to an MCP server");
    AnsiConsole.MarkupLine("  [green]version[/]           Show version information");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[bold]Options:[/]");
    AnsiConsole.MarkupLine("  [green]--config <path>[/]   Path to nexus.yaml config file");
}

static string EscapeMarkup(string text) =>
    text.Replace("[", "[[").Replace("]", "]]");
