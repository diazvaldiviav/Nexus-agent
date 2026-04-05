using Nexus.Hardware.Envelopes;

namespace Nexus.Hardware.Tests;

public class CpuEnvelopeTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var envelope = new CpuEnvelope("x86_64", 12.5, 8.0, 6.3, 16);

        Assert.Equal("x86_64", envelope.CpuArchitectureClass);
        Assert.Equal(12.5, envelope.CpuParallelismScore);
        Assert.Equal(8.0, envelope.CpuSimdScore);
        Assert.Equal(6.3, envelope.CpuInferenceScore);
        Assert.Equal(16, envelope.MaxSafeCpuThreads);
    }

    [Fact]
    public void IsViable_PositiveScore_ReturnsTrue()
    {
        var envelope = new CpuEnvelope("x86_64", 10.0, 5.0, 3.5, 8);

        Assert.True(envelope.IsViable());
    }

    [Fact]
    public void IsViable_ZeroScore_ReturnsFalse()
    {
        var envelope = new CpuEnvelope("x86_64", 10.0, 5.0, 0.0, 8);

        Assert.False(envelope.IsViable());
    }

    [Fact]
    public void IsViable_NegativeScore_ReturnsFalse()
    {
        var envelope = new CpuEnvelope("x86_64", 10.0, 5.0, -1.0, 8);

        Assert.False(envelope.IsViable());
    }

    [Fact]
    public void IsViable_NegativeMaxSafeCpuThreads_StillViableIfScorePositive()
    {
        var envelope = new CpuEnvelope("x86_64", 10.0, 5.0, 3.5, -1);

        Assert.True(envelope.IsViable());
    }

    [Fact]
    public void IsViable_ExactZeroScoreBoundary_NotViable()
    {
        var envelope = new CpuEnvelope("x86_64", 10.0, 5.0, 0.0, 8);

        Assert.False(envelope.IsViable());
    }

    [Fact]
    public void StructuralEquality_EqualValues_AreEqual()
    {
        var a = new CpuEnvelope("x86_64", 10.0, 5.0, 3.5, 8);
        var b = new CpuEnvelope("x86_64", 10.0, 5.0, 3.5, 8);

        Assert.Equal(a, b);
    }

    [Fact]
    public void StructuralEquality_DifferentValues_NotEqual()
    {
        var a = new CpuEnvelope("x86_64", 10.0, 5.0, 3.5, 8);
        var b = new CpuEnvelope("arm64", 10.0, 5.0, 3.5, 8);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void WithExpression_CreatesModifiedCopy()
    {
        var original = new CpuEnvelope("x86_64", 10.0, 5.0, 3.5, 8);

        var modified = original with { MaxSafeCpuThreads = 4 };

        Assert.Equal(8, original.MaxSafeCpuThreads);
        Assert.Equal(4, modified.MaxSafeCpuThreads);
        Assert.Equal(original.CpuArchitectureClass, modified.CpuArchitectureClass);
    }
}
