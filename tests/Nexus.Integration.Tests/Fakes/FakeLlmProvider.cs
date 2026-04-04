using System.Runtime.CompilerServices;
using Nexus.Core.Abstractions;
using Nexus.Core.Models;

namespace Nexus.Integration.Tests.Fakes;

public sealed class FakeLlmProvider : ILlmProvider
{
    private readonly Func<string, string> _responseFactory;

    public FakeLlmProvider(string providerName, Func<string, string> responseFactory)
    {
        ProviderName = providerName;
        _responseFactory = responseFactory;
    }

    public string ProviderName { get; }

    public Task<string> ChatAsync(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> conversationHistory,
        string model,
        CancellationToken cancellationToken = default)
    {
        var lastUserMessage = conversationHistory.LastOrDefault(m => m.Role == "user")?.Content ?? "";
        return Task.FromResult(_responseFactory(lastUserMessage));
    }

    public async IAsyncEnumerable<string> ChatStreamAsync(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> conversationHistory,
        string model,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var lastUserMessage = conversationHistory.LastOrDefault(m => m.Role == "user")?.Content ?? "";
        var response = _responseFactory(lastUserMessage);
        yield return response;
        await Task.CompletedTask;
    }
}
