using Nexus.Models;
using Nexus.Models.Enums;

namespace Nexus.Models.Tests;

public class CuratedCatalogTests
{
    private static readonly CuratedCatalog Catalog = new();
    private static readonly ModelNormalizer Normalizer = new();

    // ── Count / GetAllCandidates ──────────────────────────────────────────────

    [Fact]
    public void Count_Returns20()
    {
        Assert.Equal(20, Catalog.Count);
    }

    [Fact]
    public void GetAllCandidates_Returns20Items()
    {
        // Act
        var list = Catalog.GetAllCandidates();

        // Assert
        Assert.Equal(20, list.Count);
    }

    [Fact]
    public void GetAllCandidates_AllHaveNonEmptyId()
    {
        // Act
        var list = Catalog.GetAllCandidates();

        // Assert
        Assert.All(list, c => Assert.False(string.IsNullOrWhiteSpace(c.Id)));
    }

    [Fact]
    public void GetAllCandidates_AllIdsAreUnique()
    {
        // Act
        var list = Catalog.GetAllCandidates();

        // Assert
        Assert.Equal(20, list.Select(c => c.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // ── GetById ───────────────────────────────────────────────────────────────

    [Fact]
    public void GetById_KnownId_ReturnsCorrectModel()
    {
        // Act
        var candidate = Catalog.GetById("qwen3-0.6b-q4km");

        // Assert
        Assert.NotNull(candidate);
        Assert.Equal("Qwen", candidate.Family);
        Assert.Equal("3-0.6B", candidate.Variant);
        Assert.Equal(600L, candidate.ParameterCount);
    }

    [Fact]
    public void GetById_UnknownId_ReturnsNull()
    {
        // Act
        var candidate = Catalog.GetById("nonexistent");

        // Assert
        Assert.Null(candidate);
    }

    [Fact]
    public void GetById_NullId_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Catalog.GetById(null!));
    }

    // ── GetByFamily ───────────────────────────────────────────────────────────

    [Fact]
    public void GetByFamily_Qwen_Returns7Models()
    {
        // Act
        var list = Catalog.GetByFamily("Qwen");

        // Assert
        Assert.Equal(7, list.Count);
    }

    [Fact]
    public void GetByFamily_CaseInsensitive_ReturnsCorrectModels()
    {
        // Act
        var lower = Catalog.GetByFamily("qwen");
        var upper = Catalog.GetByFamily("QWEN");

        // Assert
        Assert.Equal(lower.Count, upper.Count);
    }

    [Fact]
    public void GetByFamily_UnknownFamily_ReturnsEmptyList()
    {
        // Act
        var list = Catalog.GetByFamily("UnknownFamily");

        // Assert
        Assert.NotNull(list);
        Assert.Empty(list);
    }

    // ── GetByTaskFit ──────────────────────────────────────────────────────────

    [Fact]
    public void GetByTaskFit_Chat_Returns13Models()
    {
        // Act
        var list = Catalog.GetByTaskFit(ModelTaskFit.Chat);

        // Assert
        Assert.Equal(13, list.Count);
    }

    [Fact]
    public void GetByTaskFit_Reasoning_Returns5Models()
    {
        // Act
        var list = Catalog.GetByTaskFit(ModelTaskFit.Reasoning);

        // Assert
        Assert.Equal(5, list.Count);
    }

    [Fact]
    public void GetByTaskFit_Coding_Returns2Models()
    {
        // Act
        var list = Catalog.GetByTaskFit(ModelTaskFit.Coding);

        // Assert
        Assert.Equal(2, list.Count);
    }

    // ── Distribution / Normalization ──────────────────────────────────────────

    [Fact]
    public void AllModels_HaveValidDistributionProfile()
    {
        // Act
        var list = Catalog.GetAllCandidates();

        // Assert
        Assert.All(list, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.DistributionProfile.OllamaModelTag));
            Assert.True(c.DistributionProfile.CanBeManagedByRuntime);
        });
    }

    [Fact]
    public void AllModels_NormalizeSuccessfully()
    {
        // Act
        var list = Catalog.GetAllCandidates();

        // Assert — Normalize must not throw for any catalog entry
        Assert.All(list, c =>
        {
            var profile = Normalizer.Normalize(c);
            Assert.NotNull(profile);
        });
    }
}
