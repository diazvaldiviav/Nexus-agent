using System.Runtime.CompilerServices;
using Nexus.Core.Abstractions;
using Nexus.Core.Models;

namespace Nexus.Integration.Tests.Fakes;

public sealed class FakeLlmProvider : ILlmProvider
{
    private readonly Func<string, string>? _legacyResponseFactory;
    private readonly Func<IReadOnlyList<ConversationMessage>, string>? _responseFactory;

    /// <summary>
    /// Constructor for legacy tests that use a single-string factory (receives last user message).
    /// </summary>
    public FakeLlmProvider(string providerName, Func<string, string> responseFactory)
    {
        ProviderName = providerName;
        _legacyResponseFactory = responseFactory;
        _responseFactory = null;
    }

    /// <summary>
    /// Private constructor for tests that need access to the full conversation history.
    /// Use CreateWithHistoryFactory() to create instances with full-history support.
    /// </summary>
    private FakeLlmProvider(string providerName, Func<IReadOnlyList<ConversationMessage>, string> responseFactory, bool _)
    {
        ProviderName = providerName;
        _responseFactory = responseFactory;
        _legacyResponseFactory = null;
    }

    /// <summary>
    /// Factory method for tests that need access to the full conversation history.
    /// </summary>
    public static FakeLlmProvider CreateWithHistoryFactory(
        string providerName,
        Func<IReadOnlyList<ConversationMessage>, string> responseFactory)
    {
        return new(providerName, responseFactory, default);
    }

    public string ProviderName { get; }

    public Task<string> ChatAsync(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> conversationHistory,
        string model,
        CancellationToken cancellationToken = default)
    {
        var response = _responseFactory is not null
            ? _responseFactory(conversationHistory)
            : GetLegacyResponse(conversationHistory);
        return Task.FromResult(response);
    }

    public async IAsyncEnumerable<string> ChatStreamAsync(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> conversationHistory,
        string model,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = _responseFactory is not null
            ? _responseFactory(conversationHistory)
            : GetLegacyResponse(conversationHistory);
        yield return response;
        await Task.CompletedTask;
    }

    private string GetLegacyResponse(IReadOnlyList<ConversationMessage> conversationHistory)
    {
        if (_legacyResponseFactory is null)
            return "";
        var lastUserMessage = conversationHistory.LastOrDefault(m => m.Role == "user")?.Content ?? "";
        return _legacyResponseFactory(lastUserMessage);
    }
}
