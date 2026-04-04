using System.Runtime.CompilerServices;
using Nexus.Core.Abstractions;

namespace Nexus.Desktop.Tests.Fakes;

internal sealed class FakeAgentService : IAgentService
{
    public Exception? ExceptionToThrow { get; set; }
    public List<string> TokensToYield { get; set; } = ["Hello"];
    public List<string> ReceivedMessages { get; } = [];

    public async IAsyncEnumerable<string> ChatStreamAsync(
        string userMessage,
        Action<int>? onEntitiesExtracted = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ReceivedMessages.Add(userMessage);
        if (ExceptionToThrow is not null) throw ExceptionToThrow;
        foreach (var token in TokensToYield)
        {
            yield return token;
            await Task.CompletedTask;
        }
    }

    public Task ClearHistoryAsync() =>
        ExceptionToThrow is not null ? Task.FromException(ExceptionToThrow) : Task.CompletedTask;

    public Task FlushPendingExtractionAsync() => Task.CompletedTask;
}
