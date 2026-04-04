using Nexus.Core.Abstractions;

namespace Nexus.Core.Providers;

/// <summary>
/// Resolves ILlmProvider instances by provider name.
/// Uses DI multi-registration: all ILlmProvider implementations are injected as IEnumerable.
/// </summary>
public class LlmProviderFactory
{
    private readonly Dictionary<string, ILlmProvider> _providers;

    public LlmProviderFactory(IEnumerable<ILlmProvider> providers)
    {
        _providers = providers.ToDictionary(
            p => p.ProviderName,
            p => p,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the provider for the given name, or null if not registered.
    /// </summary>
    public ILlmProvider? GetProvider(string providerName)
    {
        _providers.TryGetValue(providerName, out var provider);
        return provider;
    }

    /// <summary>
    /// Returns the provider for the given name, or throws if not registered.
    /// </summary>
    public ILlmProvider GetRequiredProvider(string providerName)
    {
        return GetProvider(providerName)
            ?? throw new InvalidOperationException(
                $"LLM provider '{providerName}' is not registered. " +
                $"Available providers: {string.Join(", ", _providers.Keys)}. " +
                $"Check your nexus.yaml configuration.");
    }
}
