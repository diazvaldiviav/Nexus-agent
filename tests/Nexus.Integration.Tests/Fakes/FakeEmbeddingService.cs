using Nexus.Memory.Abstractions;

namespace Nexus.Integration.Tests.Fakes;

public class FakeEmbeddingService : IEmbeddingService
{
    private readonly float[]? _fixedEmbedding;
    private readonly Func<string, float[]>? _embeddingFactory;
    private readonly Exception? _exception;

    public FakeEmbeddingService(float[]? fixedEmbedding = null, Exception? exception = null)
    {
        _fixedEmbedding = fixedEmbedding;
        _exception = exception;
    }

    public FakeEmbeddingService(Func<string, float[]> embeddingFactory)
    {
        _embeddingFactory = embeddingFactory;
    }

    public int CallCount { get; private set; }

    public List<string> CalledWithTexts { get; } = new();

    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        CallCount++;
        CalledWithTexts.Add(text);
        if (_exception is not null)
            throw _exception;
        if (_embeddingFactory is not null)
            return Task.FromResult(_embeddingFactory(text));
        return Task.FromResult(_fixedEmbedding ?? new float[768]);
    }
}
