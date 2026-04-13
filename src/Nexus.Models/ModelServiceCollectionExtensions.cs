using Microsoft.Extensions.DependencyInjection;

namespace Nexus.Models;

/// <summary>
/// Registers the curated catalog and model normalizer services into the DI container.
/// </summary>
public static class ModelServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="ICuratedCatalog"/> and <see cref="IModelNormalizer"/> as singletons.
    /// </summary>
    /// <param name="services">The service collection to register model services into.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
    public static IServiceCollection AddNexusModels(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ICuratedCatalog, CuratedCatalog>();
        services.AddSingleton<IModelNormalizer, ModelNormalizer>();

        return services;
    }
}
