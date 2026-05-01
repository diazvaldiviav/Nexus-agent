using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.CLI;
using Nexus.Connectors;
using Nexus.Core;
using Nexus.Core.Abstractions;
using Nexus.Core.Models;
using Nexus.Core.Services;
using Nexus.Core.Config;
using Nexus.Memory.Abstractions;
using Nexus.Memory.Graph;
using Nexus.Memory.Processing;
using Spectre.Console;

// ── Phase 1: Parse --config arg, filter args ──
var configPath = args.Length > 0 && args[0] == "--config" && args.Length > 1 ? args[1] : null;
var filteredArgs = FilterConfigArgs(args);

// ── Phase 2: Early exits ──
if (filteredArgs.Length > 0 && filteredArgs[0] == "init")
{
    await OnboardingWizard.RunAsync();
    return 0;
}

if (filteredArgs.Length > 0 && (filteredArgs[0] == "--help" || filteredArgs[0] == "-h" || filteredArgs[0] == "help"))
{
    ShowHelp();
    return 0;
}

if (filteredArgs.Length > 0 && filteredArgs[0] == "version")
{
    AnsiConsole.MarkupLine("[bold blue]Nexus Agent[/] v1.0.0-mvp");
    return 0;
}

// ── Phase 3: Auto-trigger wizard or load config ──
NexusConfig config;
if (!ConfigLoader.Exists(configPath) && !Console.IsInputRedirected && filteredArgs.Length == 0)
{
    AnsiConsole.MarkupLine("[dim]No configuration found. Starting setup wizard...[/]");
    AnsiConsole.WriteLine();
    config = await OnboardingWizard.RunAsync();
}
else
{
    config = ConfigLoader.Load(configPath);
}

// ── Phase 3b: Validate config before DI setup ──
var validationResult = ConfigValidator.Validate(config);
if (!validationResult.IsValid)
{
    foreach (var (field, error) in validationResult.Errors)
        AnsiConsole.MarkupLine($"[red]Configuration error ({Markup.Escape(field)}): {Markup.Escape(error)}[/]");
    return 1;
}

// ── Phase 4: DI setup ──
var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information)); // [DIAG-P9] temp: was Error
services.AddNexusAgent(config);
services.AddNexusMcp();
services.AddSingleton(sp => new PersistentPermissionStore(
    Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".nexus", "permissions.json"),
    sp.GetService<ILogger<PersistentPermissionStore>>()));
services.AddSingleton<IPermissionGate, CliPermissionGate>();
var sp = services.BuildServiceProvider();

// ── Phase 5: Normal command routing ──
// Check for piped stdin input
if (Console.IsInputRedirected && filteredArgs.Length == 0)
{
    var query = await Console.In.ReadToEndAsync();
    if (!string.IsNullOrWhiteSpace(query))
        return await RunSingleQueryAsync(sp, query.Trim(), config);
}

if (filteredArgs.Length == 0 || filteredArgs[0] == "chat")
{
    await RunChatAsync(sp, config);
    return 0;
}
else if (filteredArgs[0] == "memory")
{
    await RunMemoryCommandAsync(sp, filteredArgs.Skip(1).ToArray(), config);
    return 0;
}
else if (filteredArgs[0] == "connect")
{
    await RunConnectCommandAsync(sp, filteredArgs.Skip(1).ToArray(), config, configPath);
    return 0;
}
else if (filteredArgs[0] == "disconnect")
{
    await RunDisconnectCommandAsync(sp, filteredArgs.Skip(1).ToArray(), config, configPath);
    return 0;
}
else if (filteredArgs[0] == "servers")
{
    RunServersCommand(sp, config);
    return 0;
}
else
{
    // Single query mode: treat non-command args as a query
    var query = string.Join(" ", filteredArgs);
    return await RunSingleQueryAsync(sp, query, config);
}

static string[] FilterConfigArgs(string[] args)
{
    if (args.Length >= 2 && args[0] == "--config")
        return args.Skip(2).ToArray();
    return args;
}

static async Task<int> RunSingleQueryAsync(IServiceProvider sp, string query, NexusConfig? config = null)
{
    var agent = sp.GetRequiredService<AgentService>();
    var decay = sp.GetRequiredService<RelevanceDecay>();
    await decay.ApplyDecayAsync();

    if (config is not null)
        await ConnectMcpServersAsync(sp, config);

    try
    {
        await foreach (var token in agent.ChatStreamAsync(query))
        {
            Console.Write(token);
        }

        // Only add newline if not piped (for clean pipe output)
        if (!Console.IsOutputRedirected)
            Console.WriteLine();

        // Ensure entities are extracted before exit
        await agent.FlushPendingExtractionAsync();
        return 0;
    }
    catch (Exception ex)
    {
        var msg = ex is AggregateException agg
            ? agg.Flatten().InnerExceptions.FirstOrDefault()?.Message ?? ex.Message
            : ex.Message;
        await Console.Error.WriteLineAsync($"Error: {msg}");
        return 1;
    }
}

static async Task ConnectMcpServersAsync(IServiceProvider sp, NexusConfig config)
{
    if (config.Mcp.Servers.Count == 0) return;

    var lifecycle = sp.GetRequiredService<McpLifecycleService>();

    foreach (var server in config.Mcp.Servers)
    {
        try
        {
            AnsiConsole.MarkupLine($"[dim]  Connecting to MCP server [bold]{EscapeMarkup(server.Name)}[/] ({EscapeMarkup(server.Command ?? "null")} {EscapeMarkup(string.Join(" ", server.Args))})...[/]");
            var result = await lifecycle.ConnectServerAsync(server);
            if (result.Success)
            {
                AnsiConsole.MarkupLine($"[green]  ✓ MCP server [bold]{EscapeMarkup(server.Name)}[/]: {result.ToolCount} tools discovered[/]");
            }
            else
            {
                var reason = string.IsNullOrWhiteSpace(result.ErrorMessage) ? "connection failed" : result.ErrorMessage;
                AnsiConsole.MarkupLine($"[yellow]  ✗ MCP server [bold]{EscapeMarkup(server.Name)}[/]: {EscapeMarkup(reason)}[/]");
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[yellow]  ✗ MCP server [bold]{EscapeMarkup(server.Name)}[/]: {EscapeMarkup(ex.Message)}[/]");
        }
    }
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

    // Auto-connect MCP servers from config
    await ConnectMcpServersAsync(sp, config);

    AnsiConsole.MarkupLine("[dim]Type your message, or 'exit' to quit, 'clear' to clear history[/]");
    AnsiConsole.WriteLine();

    while (true)
    {
        var userInput = AnsiConsole.Ask<string>("[bold green]You>[/]");

        if (string.IsNullOrWhiteSpace(userInput)) continue;
        if (string.Equals(userInput, "exit", StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine("[dim]Saving pending entities...[/]");
            await agentService.FlushPendingExtractionAsync();
            break;
        }
        if (string.Equals(userInput, "clear", StringComparison.OrdinalIgnoreCase))
        {
            await agentService.ClearHistoryAsync();
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
            var msg = ex is AggregateException agg
                ? agg.Flatten().InnerExceptions.FirstOrDefault()?.Message ?? ex.Message
                : ex.Message;
            AnsiConsole.MarkupLine($"[red]Error: {EscapeMarkup(msg)}[/]");
        }

        AnsiConsole.WriteLine();
    }
}

static async Task RunMemoryCommandAsync(IServiceProvider sp, string[] args, NexusConfig config)
{
    var graph = sp.GetRequiredService<IKnowledgeGraph>();

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
        var interactionCount = await graph.GetInteractionCountAsync();
        AnsiConsole.MarkupLine($"[bold]Memory Statistics[/]");
        AnsiConsole.MarkupLine($"  Entities: [bold]{entities.Count}[/]");
        AnsiConsole.MarkupLine($"  With embeddings: [bold]{withEmbeddings}/{entities.Count}[/]");
        AnsiConsole.MarkupLine($"  Relations: [bold]{relations.Count}[/]");
        AnsiConsole.MarkupLine($"  Interactions: [bold]{interactionCount}[/]");
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
    else if (args[0] == "dedupe")
    {
        var resolver = sp.GetRequiredService<EntityResolver>();
        var autoMerge = args.Length > 1 && string.Equals(args[1], "--auto", StringComparison.OrdinalIgnoreCase);

        if (autoMerge)
        {
            var merged = await resolver.FindAndMergeAsync(useLlmConfirmation: false);
            if (merged.Count == 0)
            {
                AnsiConsole.MarkupLine("[dim]No duplicates found above threshold.[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"Merged {merged.Count} pair(s):");
                foreach (var entity in merged)
                    AnsiConsole.MarkupLine($"  [green]✓[/] {EscapeMarkup(entity.Name)} ({entity.Type})");
            }
        }
        else
        {
            var duplicates = await resolver.FindDuplicatesAsync();
            if (duplicates.Count == 0)
            {
                AnsiConsole.MarkupLine("[dim]No duplicates found above threshold.[/]");
            }
            else
            {
                var table = new Table();
                table.AddColumn("Entity 1");
                table.AddColumn("Entity 2");
                table.AddColumn("Similarity");
                table.AddColumn("Type");

                foreach (var pair in duplicates)
                {
                    table.AddRow(
                        EscapeMarkup(pair.Entity1.Name),
                        EscapeMarkup(pair.Entity2.Name),
                        $"{pair.Similarity:F3}",
                        pair.Entity1.Type.ToString());
                }

                AnsiConsole.Write(table);
            }
        }
    }
    else if (args[0] == "archive")
    {
        var compressor = sp.GetRequiredService<MemoryCompressor>();
        var count = await compressor.ArchiveStaleEntitiesAsync();
        var archivePath = ConfigLoader.GetArchivePath(config);
        if (count == 0)
            AnsiConsole.MarkupLine("[dim]No stale entities to archive.[/]");
        else
            AnsiConsole.MarkupLine($"[green]Archived {count} stale entities to {Markup.Escape(archivePath)}[/]");
    }
    else if (args[0] == "compress")
    {
        var compressor = sp.GetRequiredService<MemoryCompressor>();
        var count = await compressor.CompressSummariesAsync();
        if (count == 0)
            AnsiConsole.MarkupLine("[dim]No interactions to compress.[/]");
        else
            AnsiConsole.MarkupLine($"[green]Compressed {count} interactions into weekly/monthly summaries.[/]");
    }
    else
    {
        AnsiConsole.MarkupLine("Usage: nexus memory [list|stats|dedupe|dedupe --auto|archive|compress]");
    }
}

static async Task RunConnectCommandAsync(IServiceProvider sp, string[] args, NexusConfig config, string? configPath)
{
    if (args.Length < 2)
    {
        AnsiConsole.MarkupLine("Usage: nexus connect <name> <command> [args...]");
        AnsiConsole.MarkupLine("  Example: nexus connect filesystem npx -y @modelcontextprotocol/server-filesystem /tmp");
        return;
    }

    var name = args[0];
    var command = args[1];
    var commandArgs = args.Skip(2).ToList();

    var entry = new McpServerEntry
    {
        Name = name,
        Transport = "stdio",
        Command = command,
        Args = commandArgs
    };

    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .StartAsync($"Connecting to {EscapeMarkup(name)}...", async ctx =>
        {
            var lifecycle = sp.GetRequiredService<McpLifecycleService>();
            var result = await lifecycle.ConnectServerAsync(entry);

            if (result.Success)
            {
                AnsiConsole.MarkupLine($"[green]Connected to MCP server [bold]{EscapeMarkup(name)}[/] ({result.ToolCount} tools discovered)[/]");

                // Persist to config
                try
                {
                    config.Mcp.Servers.RemoveAll(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
                    config.Mcp.Servers.Add(entry);
                    ConfigLoader.Save(config, configPath);
                    AnsiConsole.MarkupLine("[dim]Server saved to config.[/]");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[yellow]Warning: Could not save to config — server is connected for this session only: {EscapeMarkup(ex.Message)}[/]");
                }
            }
            else
            {
                var reason = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? "Check that the command is available."
                    : result.ErrorMessage;
                AnsiConsole.MarkupLine($"[yellow]Could not connect to [bold]{EscapeMarkup(name)}[/]: {EscapeMarkup(reason)}[/]");
            }
        });
}

static async Task RunDisconnectCommandAsync(IServiceProvider sp, string[] args, NexusConfig config, string? configPath)
{
    if (args.Length < 1)
    {
        AnsiConsole.MarkupLine("Usage: nexus disconnect <name>");
        return;
    }

    var name = args[0];

    var exists = config.Mcp.Servers.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
    if (!exists)
    {
        AnsiConsole.MarkupLine($"[yellow]MCP server [bold]{EscapeMarkup(name)}[/] is not configured.[/]");
        return;
    }

    var lifecycle = sp.GetRequiredService<McpLifecycleService>();
    await lifecycle.DisconnectServerAsync(name);

    config.Mcp.Servers.RemoveAll(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    AnsiConsole.MarkupLine($"[green]Disconnected and removed MCP server [bold]{EscapeMarkup(name)}[/].[/]");

    try
    {
        ConfigLoader.Save(config, configPath);
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[yellow]Warning: Could not save to config — server removed for this session only: {EscapeMarkup(ex.Message)}[/]");
    }
}

static void RunServersCommand(IServiceProvider sp, NexusConfig config)
{
    if (config.Mcp.Servers.Count == 0)
    {
        AnsiConsole.MarkupLine("[dim]No MCP servers configured. Use 'nexus connect' to add one.[/]");
        return;
    }

    var lifecycle = sp.GetRequiredService<McpLifecycleService>();
    var statuses = lifecycle.GetServerStatuses(config.Mcp.Servers);

    var table = new Table();
    table.AddColumn("Name");
    table.AddColumn("Transport");
    table.AddColumn("Command/URL");
    table.AddColumn("Status");

    foreach (var server in statuses)
    {
        var statusText = server.IsConnected
            ? $"[green]Connected ({server.ToolCount} tools)[/]"
            : "[dim]Disconnected[/]";

        table.AddRow(
            EscapeMarkup(server.ServerName),
            EscapeMarkup(server.Transport),
            EscapeMarkup(server.CommandOrUrl),
            statusText);
    }

    AnsiConsole.Write(table);
}

static void ShowHelp()
{
    AnsiConsole.Write(new FigletText("Nexus Agent").Color(Color.Blue));
    AnsiConsole.MarkupLine("[bold]Usage:[/] nexus [command] [options]");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[bold]Commands:[/]");
    AnsiConsole.MarkupLine("  [green]chat[/]              Start interactive chat (default)");
    AnsiConsole.MarkupLine("  [green]init[/]              Run the first-use setup wizard");
    AnsiConsole.MarkupLine("  [green]\"question\"[/]        Single query mode (streams response and exits)");
    AnsiConsole.MarkupLine("  [green]memory list[/]       List all entities in memory");
    AnsiConsole.MarkupLine("  [green]memory stats[/]      Show memory statistics");
    AnsiConsole.MarkupLine("  [green]memory dedupe[/]     Find duplicate entities");
    AnsiConsole.MarkupLine("  [green]memory dedupe --auto[/]  Auto-merge duplicate entities");
    AnsiConsole.MarkupLine("  [green]memory archive[/]    Archive stale entities to disk");
    AnsiConsole.MarkupLine("  [green]memory compress[/]   Compress old interactions into weekly/monthly summaries");
    AnsiConsole.MarkupLine("  [green]connect <name> <command> [args...][/]  Connect to an MCP server via stdio");
    AnsiConsole.MarkupLine("  [green]disconnect <name>[/]    Disconnect and remove an MCP server");
    AnsiConsole.MarkupLine("  [green]servers[/]              List configured MCP servers and status");
    AnsiConsole.MarkupLine("  [green]version[/]           Show version information");
    AnsiConsole.MarkupLine("  [green]help[/]              Show this help message");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[bold]Options:[/]");
    AnsiConsole.MarkupLine("  [green]--config <path>[/]   Path to nexus.yaml config file");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[bold]Pipe support:[/]");
    AnsiConsole.MarkupLine("  echo \"question\" | nexus");
    AnsiConsole.MarkupLine("  nexus \"question\" | other_tool");
}

static string EscapeMarkup(string text) =>
    text.Replace("[", "[[").Replace("]", "]]");
