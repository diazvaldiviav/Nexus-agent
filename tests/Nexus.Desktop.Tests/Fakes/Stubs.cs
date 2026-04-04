using System.Runtime.CompilerServices;
using Nexus.Core.Abstractions;
using Nexus.Core.Models;
using Nexus.Core.Providers;
using Nexus.Memory.Abstractions;
using Nexus.Memory.Processing;
using Nexus.Memory.Models;

namespace Nexus.Desktop.Tests.Fakes;

internal sealed class StubEmbeddingService : IEmbeddingService
{
    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        => Task.FromResult(new float[768]);
}

internal sealed class StubLlmClient : ILlmClient
{
    public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
        => Task.FromResult(string.Empty);
}

internal sealed class StubLlmProvider : ILlmProvider
{
    public string ProviderName => "stub";

    public Task<string> ChatAsync(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> conversationHistory,
        string model,
        CancellationToken cancellationToken = default)
        => Task.FromResult(string.Empty);

    public async IAsyncEnumerable<string> ChatStreamAsync(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> conversationHistory,
        string model,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return string.Empty;
        await Task.CompletedTask;
    }
}

internal sealed class StubInteractionSummarizer : IInteractionSummarizer
{
    public Task<Interaction> SummarizeAsync(
        string conversationText,
        string summaryPrompt,
        List<string>? referencedEntityIds = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new Interaction());
}
