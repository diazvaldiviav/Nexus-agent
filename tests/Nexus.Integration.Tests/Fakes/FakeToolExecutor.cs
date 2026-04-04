using Nexus.Core.Abstractions;

namespace Nexus.Integration.Tests.Fakes;

public sealed class FakeToolExecutor : IToolExecutor
{
    private readonly Func<string, string, Dictionary<string, object>?, string>? _handler;

    public FakeToolExecutor(Func<string, string, Dictionary<string, object>?, string>? handler = null)
    {
        _handler = handler;
    }

    public bool HasTools => true;

    public string GetToolDefinitionsForPrompt() =>
        "- read_file: Reads a file\n  Parameters: {\"path\": \"string\"}";

    public Task<string> InvokeToolAsync(
        string serverName,
        string toolName,
        Dictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_handler?.Invoke(serverName, toolName, parameters)
            ?? $"Content of {toolName}");
    }
}
