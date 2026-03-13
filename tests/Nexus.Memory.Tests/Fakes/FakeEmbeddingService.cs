namespace Nexus.Memory.Tests.Fakes;

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

    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        CallCount++;
        CalledWithTexts.Add(text);
        if (_exception is not null)
            throw _exception;
        return Task.FromResult(_fixedEmbedding ?? new float[768]);
    }
}
