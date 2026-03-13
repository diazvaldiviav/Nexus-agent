using Nexus.Memory;

namespace Nexus.Integration.Tests.Fakes;

public sealed class MockLlmClient : ILlmClient
{
    private readonly Func<string, Task<string>> _handler;
    public string? LastPrompt { get; private set; }

    public MockLlmClient(Func<string, Task<string>> handler)
    {
        _handler = handler;
    }

    public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        LastPrompt = prompt;
        return _handler(prompt);
    }
}
