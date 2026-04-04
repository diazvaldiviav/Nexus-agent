namespace Nexus.Core.Abstractions;

public interface IAgentService
{
    IAsyncEnumerable<string> ChatStreamAsync(
        string userMessage,
        Action<int>? onEntitiesExtracted = null,
        CancellationToken cancellationToken = default);

    Task ClearHistoryAsync();
    Task FlushPendingExtractionAsync();
}
