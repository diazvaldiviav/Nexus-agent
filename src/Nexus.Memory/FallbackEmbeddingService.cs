using Microsoft.Extensions.Logging;

namespace Nexus.Memory;

public class FallbackEmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingService _primary;
    private readonly IEmbeddingService? _fallback;
    private readonly ILogger<FallbackEmbeddingService>? _logger;

    public FallbackEmbeddingService(
        IEmbeddingService primary,
        IEmbeddingService? fallback = null,
        ILogger<FallbackEmbeddingService>? logger = null)
    {
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        _fallback = fallback;
        _logger = logger;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _primary.GenerateEmbeddingAsync(text, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (_fallback is not null)
        {
            _logger?.LogWarning(ex, "Primary embedding service failed. Falling back to cloud provider.");
            return await _fallback.GenerateEmbeddingAsync(text, cancellationToken).ConfigureAwait(false);
        }
    }
}
