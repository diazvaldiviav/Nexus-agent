using Nexus.Connectors.ToolFiltering;

namespace Nexus.Integration.Tests;

public class ToolCapabilityResolverTests
{
    // Tier breakpoints (from ToolCapabilityResolver):
    //   < 4B  → ChatOnly  (cannot reliably emit tool-call JSON; planner is skipped)
    //   < 8B  → Limited   (small models — Complex tools excluded, edit_file overridden)
    //   < 30B → Capable   (mid models — handle moderate schemas reliably)
    //   ≥ 30B → Full      (large models — full tool-use)

    // -------------------------------------------------------------------------
    // ChatOnly tier (< 4B)
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolve_SubOneBDecimal_ReturnsChatOnly()
    {
        var result = ToolCapabilityResolver.Resolve("qwen3:0.6b");
        Assert.Equal(ToolCallingTier.ChatOnly, result);
    }

    [Fact]
    public void Resolve_SmallDecimal_ReturnsChatOnly()
    {
        var result = ToolCapabilityResolver.Resolve("qwen3:1.7b");
        Assert.Equal(ToolCallingTier.ChatOnly, result);
    }

    [Fact]
    public void Resolve_TwoB_ReturnsChatOnly()
    {
        var result = ToolCapabilityResolver.Resolve("gemma2:2b");
        Assert.Equal(ToolCallingTier.ChatOnly, result);
    }

    [Fact]
    public void Resolve_ThreeBWithSuffix_ReturnsChatOnly()
    {
        var result = ToolCapabilityResolver.Resolve("llama3.2:3b-instruct");
        Assert.Equal(ToolCallingTier.ChatOnly, result);
    }

    [Fact]
    public void Resolve_HyphenSeparatorThreeB_ReturnsChatOnly()
    {
        var result = ToolCapabilityResolver.Resolve("llama3.2-3b");
        Assert.Equal(ToolCallingTier.ChatOnly, result);
    }

    // -------------------------------------------------------------------------
    // Limited tier (4B ≤ b < 8B)
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolve_BoundaryFour_ReturnsLimited()
    {
        var result = ToolCapabilityResolver.Resolve("Qwen3.5:4B");
        Assert.Equal(ToolCallingTier.Limited, result);
    }

    [Fact]
    public void Resolve_SevenB_ReturnsLimited()
    {
        var result = ToolCapabilityResolver.Resolve("mistral:7b");
        Assert.Equal(ToolCallingTier.Limited, result);
    }

    // -------------------------------------------------------------------------
    // Capable tier (8B ≤ b < 30B)
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolve_BoundaryEight_ReturnsCapable()
    {
        var result = ToolCapabilityResolver.Resolve("qwen3:8b");
        Assert.Equal(ToolCallingTier.Capable, result);
    }

    [Fact]
    public void Resolve_FourteenB_ReturnsCapable()
    {
        var result = ToolCapabilityResolver.Resolve("qwen3:14b");
        Assert.Equal(ToolCallingTier.Capable, result);
    }

    [Fact]
    public void Resolve_UppercaseEightB_ReturnsCapable()
    {
        var result = ToolCapabilityResolver.Resolve("QWEN3:8B");
        Assert.Equal(ToolCallingTier.Capable, result);
    }

    [Fact]
    public void Resolve_TwentyTwoB_ReturnsCapable()
    {
        var result = ToolCapabilityResolver.Resolve("mistral-small:22b");
        Assert.Equal(ToolCallingTier.Capable, result);
    }

    // -------------------------------------------------------------------------
    // Full tier (≥ 30B)
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolve_BoundaryThirty_ReturnsFull()
    {
        var result = ToolCapabilityResolver.Resolve("qwen3:30b");
        Assert.Equal(ToolCallingTier.Full, result);
    }

    [Fact]
    public void Resolve_LargeModel_ReturnsFull()
    {
        var result = ToolCapabilityResolver.Resolve("llama3:70b");
        Assert.Equal(ToolCallingTier.Full, result);
    }

    // -------------------------------------------------------------------------
    // Defaults / edge cases
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolve_Null_ReturnsFull()
    {
        var result = ToolCapabilityResolver.Resolve(null);
        Assert.Equal(ToolCallingTier.Full, result);
    }

    [Fact]
    public void Resolve_Empty_ReturnsFull()
    {
        var result = ToolCapabilityResolver.Resolve("");
        Assert.Equal(ToolCallingTier.Full, result);
    }

    [Fact]
    public void Resolve_NoParamPattern_ReturnsFull()
    {
        var result = ToolCapabilityResolver.Resolve("mystery-model");
        Assert.Equal(ToolCallingTier.Full, result);
    }

    [Fact]
    public void Resolve_BitSuffix_ReturnsFull()
    {
        // Negative lookahead guard — "3bit" is not "3b"
        var result = ToolCapabilityResolver.Resolve("qwen3:3bit");
        Assert.Equal(ToolCallingTier.Full, result);
    }
}
