using Microsoft.Data.Sqlite;
using Nexus.Core;
using Nexus.Core.Abstractions;
using Nexus.Core.Models;
using Nexus.Core.Providers;
using Nexus.Core.Services;
using Nexus.Core.Config;
using Nexus.Integration.Tests.Fakes;
using Nexus.Memory.Abstractions;
using Nexus.Memory.Embedding;
using Nexus.Memory.Graph;
using Nexus.Memory.Infrastructure;
using Nexus.Memory.Processing;

namespace Nexus.Integration.Tests;

/// <summary>
/// Integration tests for doom loop detection in AgentService.
/// AC-6: 2 consecutive identical tool+args calls trigger doom loop,
///        inject [DoomLoop] message, give LLM one last-chance call, then break.
/// </summary>
public class DoomLoopTests : IAsyncLifetime
{
    private readonly string _dbPath;
    private readonly KnowledgeGraph _graph;
    private readonly string _connectionString;
    private AgentService? _lastAgent;

    public DoomLoopTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"doom_loop_test_{Guid.NewGuid():N}.db");
        var dbInit = new DatabaseInitializer(_dbPath);
        dbInit.Initialize();
        _connectionString = dbInit.ConnectionString;
        _graph = new KnowledgeGraph(_connectionString);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_lastAgent is not null)
        {
            await _lastAgent.FlushPendingExtractionAsync().ConfigureAwait(false);
        }

        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    private AgentService CreateAgent(
        Func<string, string> responseFactory,
        IToolExecutor? toolExecutor = null,
        NexusConfig? config = null)
    {
        // AC-H1: disable Phase 9 defaults — doom-loop tests assert exact callCount == 3 and
        // rely on undecorated tool-result strings for doom-loop signature matching;
        // [VerificationWarning] decoration would corrupt the signature and break detection.
        config ??= new NexusConfig();
        config.Mcp.PlannerContextEnabled = false;
        config.Mcp.ToolVerificationEnabled = false;
        var search = new SemanticSearch(_connectionString);
        var memoryBuilder = new MemoryContextBuilder(_graph, search);
        var promptBuilder = new PromptBuilder(memoryBuilder, config.Agent, toolExecutor);
        var modelRouter = new ModelRouter(config.Models.Routing);
        var entityExtractor = new EntityExtractor(_graph);
        var summarizer = new InteractionSummarizer(_graph);
        var fakeProvider = new FakeLlmProvider("ollama", responseFactory);
        var providerFactory = new LlmProviderFactory(new ILlmProvider[] { fakeProvider });
        var agent = new AgentService(config, _graph, promptBuilder, modelRouter,
            entityExtractor, providerFactory, summarizer, toolExecutor: toolExecutor);
        _lastAgent = agent;
        return agent;
    }

    [Fact]
    public async Task ChatAsync_SameToolSameArgs_Twice_BreaksLoop()
    {
        // Arrange
        // Flow:
        //   LLM call 1 → returns tool call (read_file /test.txt)
        //   Tool executes → signature stored
        //   LLM call 2 → returns identical tool call (read_file /test.txt)
        //   Tool executes → signature matches → doom loop fires
        //   [DoomLoop] message injected into history
        //   LLM call 3 (last-chance) → returns normal text (no tool call)
        //   Loop breaks; final response = normal text

        var callCount = 0;
        const string toolCallResponse = """[TOOL_CALL: {"name":"read_file","arguments":{"path":"/test.txt"}}]""";
        const string finalAnswer = "Here is my final answer based on what I know.";

        var fakeToolExecutor = new FakeToolExecutor((_, _, _) => "file content here");

        var agent = CreateAgent(lastUserMessage =>
        {
            callCount++;
            return callCount <= 2 ? toolCallResponse : finalAnswer;
        }, fakeToolExecutor);

        // Act
        var response = await agent.ChatAsync("Read the test file");

        // Assert: doom loop fired, final response is the normal text (no tool call marker)
        Assert.Equal(3, callCount);
        Assert.DoesNotContain("[TOOL_CALL", response.Content);
        Assert.Contains("final answer", response.Content);
    }

    [Fact]
    public async Task ChatAsync_DifferentTools_NoDoomLoop()
    {
        // Arrange
        // Flow:
        //   LLM call 1 → read_file (signature: "read_file:{...}")
        //   Tool executes → previousToolSignature = "read_file:{...}"
        //   LLM call 2 → list_directory (signature: "list_directory:{...}")
        //   Signatures differ → no doom loop
        //   LLM call 3 → normal text
        //   Loop breaks naturally

        var callCount = 0;
        const string readFileCall = """[TOOL_CALL: {"name":"read_file","arguments":{"path":"/a.txt"}}]""";
        const string listDirCall = """[TOOL_CALL: {"name":"list_directory","arguments":{"path":"/"}}]""";
        const string finalAnswer = "Final answer after both tools.";

        var toolInvocations = new List<string>();
        var fakeToolExecutor = new FakeToolExecutor((_, toolName, _) =>
        {
            toolInvocations.Add(toolName);
            return $"Result from {toolName}";
        });

        var agent = CreateAgent(lastUserMessage =>
        {
            callCount++;
            return callCount switch
            {
                1 => readFileCall,
                2 => listDirCall,
                _ => finalAnswer
            };
        }, fakeToolExecutor);

        // Act
        var response = await agent.ChatAsync("Read a file and list a directory");

        // Assert: both tools executed, no doom loop, final response is normal text
        Assert.Equal(3, callCount);
        Assert.Contains("read_file", toolInvocations);
        Assert.Contains("list_directory", toolInvocations);
        Assert.DoesNotContain("[TOOL_CALL", response.Content);
        Assert.Contains("Final answer", response.Content);
    }

    [Fact]
    public async Task ChatStreamAsync_SameToolSameArgs_Twice_BreaksLoop()
    {
        // Arrange: streaming variant of ChatAsync_SameToolSameArgs_Twice_BreaksLoop
        // Flow:
        //   LLM call 1 → tool call (read_file /test.txt)
        //   Tool executes → signature stored
        //   LLM call 2 → identical tool call → doom loop fires
        //   [DoomLoop] message injected
        //   LLM call 3 (last-chance) → normal text
        var callCount = 0;
        const string toolCallResponse = """[TOOL_CALL: {"name":"read_file","arguments":{"path":"/test.txt"}}]""";
        const string finalAnswer = "Here is my final answer based on what I know.";

        var fakeToolExecutor = new FakeToolExecutor((_, _, _) => "file content here");

        var agent = CreateAgent(lastUserMessage =>
        {
            callCount++;
            return callCount <= 2 ? toolCallResponse : finalAnswer;
        }, fakeToolExecutor);

        // Act: collect streamed tokens
        var tokens = new List<string>();
        await foreach (var token in agent.ChatStreamAsync("Read the test file"))
        {
            tokens.Add(token);
        }
        var fullOutput = string.Join("", tokens);

        // Assert: doom loop fired, final answer streamed after last-chance call
        Assert.Equal(3, callCount);
        Assert.Contains("[Executing tool: read_file...]", fullOutput);
        Assert.Contains("final answer", fullOutput);
    }

    [Fact]
    public async Task ChatAsync_ThreeToolCalls_DifferentTools_NoDoomLoop()
    {
        // Arrange: 3 different tools in sequence, no doom loop
        // Flow:
        //   LLM call 1 → read_file
        //   LLM call 2 → list_directory
        //   LLM call 3 → search (max iterations = 3, so this is the last tool)
        //   MaxToolCallIterations reached → loop exits with last tool result context
        var callCount = 0;
        const string readFileCall = """[TOOL_CALL: {"name":"read_file","arguments":{"path":"/a.txt"}}]""";
        const string listDirCall = """[TOOL_CALL: {"name":"list_directory","arguments":{"path":"/"}}]""";
        const string searchCall = """[TOOL_CALL: {"name":"search","arguments":{"query":"test"}}]""";

        var toolInvocations = new List<string>();
        var fakeToolExecutor = new FakeToolExecutor((_, toolName, _) =>
        {
            toolInvocations.Add(toolName);
            return $"Result from {toolName}";
        });

        var agent = CreateAgent(lastUserMessage =>
        {
            callCount++;
            return callCount switch
            {
                1 => readFileCall,
                2 => listDirCall,
                3 => searchCall,
                _ => "Final answer after all three tools."
            };
        }, fakeToolExecutor);

        // Act
        var response = await agent.ChatAsync("Read file, list dir, and search");

        // Assert: all 3 tools invoked, no doom loop (all different signatures)
        Assert.Equal(3, toolInvocations.Count);
        Assert.Contains("read_file", toolInvocations);
        Assert.Contains("list_directory", toolInvocations);
        Assert.Contains("search", toolInvocations);
    }

    [Fact]
    public async Task ChatAsync_SameToolDifferentArgs_NoDoomLoop()
    {
        // Arrange
        // Flow:
        //   LLM call 1 → read_file /a.txt (signature: "read_file:{\"path\":\"/a.txt\"}")
        //   Tool executes → previousToolSignature = "read_file:{...a.txt...}"
        //   LLM call 2 → read_file /b.txt (signature: "read_file:{\"path\":\"/b.txt\"}")
        //   Signatures differ (different args) → no doom loop
        //   LLM call 3 → normal text
        //   Loop breaks naturally

        var callCount = 0;
        const string readFileA = """[TOOL_CALL: {"name":"read_file","arguments":{"path":"/a.txt"}}]""";
        const string readFileB = """[TOOL_CALL: {"name":"read_file","arguments":{"path":"/b.txt"}}]""";
        const string finalAnswer = "Final answer after reading both files.";

        var toolInvocations = new List<string>();
        var fakeToolExecutor = new FakeToolExecutor((_, toolName, args) =>
        {
            var path = args?.TryGetValue("path", out var p) == true ? p?.ToString() ?? "" : "";
            toolInvocations.Add(path);
            return $"Content of {path}";
        });

        var agent = CreateAgent(lastUserMessage =>
        {
            callCount++;
            return callCount switch
            {
                1 => readFileA,
                2 => readFileB,
                _ => finalAnswer
            };
        }, fakeToolExecutor);

        // Act
        var response = await agent.ChatAsync("Read two different files");

        // Assert: both tool calls executed, signatures differed, no doom loop
        Assert.Equal(3, callCount);
        Assert.Equal(2, toolInvocations.Count);
        Assert.Contains("/a.txt", toolInvocations);
        Assert.Contains("/b.txt", toolInvocations);
        Assert.DoesNotContain("[TOOL_CALL", response.Content);
        Assert.Contains("Final answer", response.Content);
    }
}
