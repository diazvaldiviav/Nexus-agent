using Nexus.Connectors.ToolFiltering;

namespace Nexus.Integration.Tests;

public class ToolCapabilityResolverTests
{
    // -------------------------------------------------------------------------
    // 1. sub-1B decimal → Limited
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolve_SubOneBDecimal_ReturnsLimited()
    {
        // Arrange
        const string modelName = "qwen3:0.6b";

        // Act
        var result = ToolCapabilityResolver.Resolve(modelName);

        // Assert
        Assert.Equal(ToolCallingTier.Limited, result);
    }

    // -------------------------------------------------------------------------
    // 2. decimal < 3 → Limited
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolve_DecimalLessThanThree_ReturnsLimited()
    {
        // Arrange
        const string modelName = "qwen3:1.7b";

        // Act
        var result = ToolCapabilityResolver.Resolve(modelName);

        // Assert
        Assert.Equal(ToolCallingTier.Limited, result);
    }

    // -------------------------------------------------------------------------
    // 3. integer < 3 → Limited
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolve_IntegerLessThanThree_ReturnsLimited()
    {
        // Arrange
        const string modelName = "gemma2:2b";

        // Act
        var result = ToolCapabilityResolver.Resolve(modelName);

        // Assert
        Assert.Equal(ToolCallingTier.Limited, result);
    }

    // -------------------------------------------------------------------------
    // 4. boundary 3, suffix after b → Capable
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolve_BoundaryThreeWithSuffix_ReturnsCapable()
    {
        // Arrange
        const string modelName = "llama3.2:3b-instruct";

        // Act
        var result = ToolCapabilityResolver.Resolve(modelName);

        // Assert
        Assert.Equal(ToolCallingTier.Capable, result);
    }

    // -------------------------------------------------------------------------
    // 5. mid-range → Capable
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolve_MidRange_ReturnsCapable()
    {
        // Arrange
        const string modelName = "mistral:7b";

        // Act
        var result = ToolCapabilityResolver.Resolve(modelName);

        // Assert
        Assert.Equal(ToolCallingTier.Capable, result);
    }

    // -------------------------------------------------------------------------
    // 6. boundary 8 → Full
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolve_BoundaryEight_ReturnsFull()
    {
        // Arrange
        const string modelName = "qwen3:8b";

        // Act
        var result = ToolCapabilityResolver.Resolve(modelName);

        // Assert
        Assert.Equal(ToolCallingTier.Full, result);
    }

    // -------------------------------------------------------------------------
    // 7. two-digit → Full
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolve_TwoDigit_ReturnsFull()
    {
        // Arrange
        const string modelName = "qwen3:14b";

        // Act
        var result = ToolCapabilityResolver.Resolve(modelName);

        // Assert
        Assert.Equal(ToolCallingTier.Full, result);
    }

    // -------------------------------------------------------------------------
    // 8. case-insensitive → Full
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolve_Uppercase_ReturnsFull()
    {
        // Arrange
        const string modelName = "QWEN3:8B";

        // Act
        var result = ToolCapabilityResolver.Resolve(modelName);

        // Assert
        Assert.Equal(ToolCallingTier.Full, result);
    }

    // -------------------------------------------------------------------------
    // 9. hyphen separator → Capable
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolve_HyphenSeparator_ReturnsCapable()
    {
        // Arrange
        const string modelName = "llama3.2-3b";

        // Act
        var result = ToolCapabilityResolver.Resolve(modelName);

        // Assert
        Assert.Equal(ToolCallingTier.Capable, result);
    }

    // -------------------------------------------------------------------------
    // 10. null → Full (safe default)
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolve_Null_ReturnsFull()
    {
        // Arrange
        // modelName is null

        // Act
        var result = ToolCapabilityResolver.Resolve(null);

        // Assert
        Assert.Equal(ToolCallingTier.Full, result);
    }

    // -------------------------------------------------------------------------
    // 11. empty string → Full
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolve_Empty_ReturnsFull()
    {
        // Arrange
        const string modelName = "";

        // Act
        var result = ToolCapabilityResolver.Resolve(modelName);

        // Assert
        Assert.Equal(ToolCallingTier.Full, result);
    }

    // -------------------------------------------------------------------------
    // 12. no Nb pattern → Full
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolve_NoParamPattern_ReturnsFull()
    {
        // Arrange
        const string modelName = "mystery-model";

        // Act
        var result = ToolCapabilityResolver.Resolve(modelName);

        // Assert
        Assert.Equal(ToolCallingTier.Full, result);
    }

    // -------------------------------------------------------------------------
    // 13. negative lookahead guard — "3bit" is not "3b" → Full
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolve_BitSuffix_ReturnsFull()
    {
        // Arrange
        const string modelName = "qwen3:3bit";

        // Act
        var result = ToolCapabilityResolver.Resolve(modelName);

        // Assert
        Assert.Equal(ToolCallingTier.Full, result);
    }
}
