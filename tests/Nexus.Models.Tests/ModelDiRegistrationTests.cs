using Microsoft.Extensions.DependencyInjection;
using Nexus.Models;

namespace Nexus.Models.Tests;

[Trait("Category", "Integration")]
public class ModelDiRegistrationTests : IDisposable
{
    private readonly ServiceProvider _provider;

    public ModelDiRegistrationTests()
    {
        var services = new ServiceCollection();
        services.AddNexusModels();
        _provider = services.BuildServiceProvider();
    }

    [Fact]
    public void Resolve_ICuratedCatalog_ReturnsCuratedCatalog()
    {
        var catalog = _provider.GetRequiredService<ICuratedCatalog>();
        Assert.IsType<CuratedCatalog>(catalog);
    }

    [Fact]
    public void Resolve_IModelNormalizer_ReturnsModelNormalizer()
    {
        var normalizer = _provider.GetRequiredService<IModelNormalizer>();
        Assert.IsType<ModelNormalizer>(normalizer);
    }

    [Fact]
    public void ICuratedCatalog_IsSingleton_ReturnsSameInstance()
    {
        var first = _provider.GetRequiredService<ICuratedCatalog>();
        var second = _provider.GetRequiredService<ICuratedCatalog>();
        Assert.Same(first, second);
    }

    [Fact]
    public void IModelNormalizer_IsSingleton_ReturnsSameInstance()
    {
        var first = _provider.GetRequiredService<IModelNormalizer>();
        var second = _provider.GetRequiredService<IModelNormalizer>();
        Assert.Same(first, second);
    }

    [Fact]
    public void Resolved_Catalog_Has20Entries()
    {
        var catalog = _provider.GetRequiredService<ICuratedCatalog>();
        Assert.Equal(20, catalog.Count);
    }

    [Fact]
    public void Resolved_Normalizer_CanNormalizeCatalogEntry()
    {
        var catalog = _provider.GetRequiredService<ICuratedCatalog>();
        var normalizer = _provider.GetRequiredService<IModelNormalizer>();

        var entry = catalog.GetAllCandidates()[0];
        var profile = normalizer.Normalize(entry);

        Assert.NotNull(profile);
        Assert.True(profile.EstimatedRamOnLoad > 0);
    }

    public void Dispose() => _provider.Dispose();
}
