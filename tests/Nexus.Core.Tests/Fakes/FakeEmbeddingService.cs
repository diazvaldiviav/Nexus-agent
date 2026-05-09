using Nexus.Memory.Abstractions;

namespace Nexus.Core.Tests.Fakes;

/// <summary>
/// Deterministic IEmbeddingService fake for unit tests.
/// Returns a fixed embedding vector (or throws a scripted exception).
/// Copied locally from Nexus.Memory.Tests/Fakes/ to keep test-project isolation.
/// </summary>
public class FakeEmbeddingService : IEmbeddingService
{
    private readonly float[]? _fixedEmbedding;
    private readonly Exception? _exception;

    public FakeEmbeddingService(float[]? fixedEmbedding = null, Exception? exception = null)
    {
        _fixedEmbedding = fixedEmbedding;
        _exception = exception;
    }

    public int CallCount { get; private set; }

    public List<string> CalledWithTexts { get; } = new();

    public CancellationToken? LastCancellationToken { get; private set; }

    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        CallCount++;
        CalledWithTexts.Add(text);
        LastCancellationToken = cancellationToken;
        if (_exception is not null)
            throw _exception;
        return Task.FromResult(_fixedEmbedding ?? new float[768]);
    }
}
