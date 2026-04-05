using Nexus.Hardware.Envelopes;
using Nexus.Hardware.States;

namespace Nexus.Hardware.Tests;

public class RamEnvelopeTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var envelope = new RamEnvelope(16_000_000_000L, 12_000_000_000L, 8_000_000_000L, PressureLevel.Low);

        Assert.Equal(16_000_000_000L, envelope.UsableRamNow);
        Assert.Equal(12_000_000_000L, envelope.SafeModelRamBudget);
        Assert.Equal(8_000_000_000L, envelope.SafeInferenceRamBudget);
        Assert.Equal(PressureLevel.Low, envelope.RamPressureLevel);
    }

    [Fact]
    public void IsViable_PositiveBudget_ReturnsTrue()
    {
        var envelope = new RamEnvelope(16_000_000_000L, 12_000_000_000L, 8_000_000_000L, PressureLevel.Low);

        Assert.True(envelope.IsViable());
    }

    [Fact]
    public void IsViable_ZeroBudget_ReturnsFalse()
    {
        var envelope = new RamEnvelope(16_000_000_000L, 0, 8_000_000_000L, PressureLevel.Critical);

        Assert.False(envelope.IsViable());
    }

    [Fact]
    public void IsViable_NegativeBudget_ReturnsFalse()
    {
        var envelope = new RamEnvelope(16_000_000_000L, -1, 8_000_000_000L, PressureLevel.Critical);

        Assert.False(envelope.IsViable());
    }

    [Fact]
    public void IsViable_NegativeUsableRamNow_StillViableIfBudgetPositive()
    {
        var envelope = new RamEnvelope(-1_000_000_000L, 8_000_000_000L, 4_000_000_000L, PressureLevel.High);

        Assert.True(envelope.IsViable());
    }

    [Fact]
    public void StructuralEquality_EqualValues_AreEqual()
    {
        var a = new RamEnvelope(16_000_000_000L, 12_000_000_000L, 8_000_000_000L, PressureLevel.Low);
        var b = new RamEnvelope(16_000_000_000L, 12_000_000_000L, 8_000_000_000L, PressureLevel.Low);

        Assert.Equal(a, b);
    }

    [Fact]
    public void StructuralEquality_DifferentValues_NotEqual()
    {
        var a = new RamEnvelope(16_000_000_000L, 12_000_000_000L, 8_000_000_000L, PressureLevel.Low);
        var b = new RamEnvelope(16_000_000_000L, 10_000_000_000L, 8_000_000_000L, PressureLevel.Low);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void WithExpression_CreatesModifiedCopy()
    {
        var original = new RamEnvelope(16_000_000_000L, 12_000_000_000L, 8_000_000_000L, PressureLevel.Low);

        var modified = original with { RamPressureLevel = PressureLevel.High };

        Assert.Equal(PressureLevel.Low, original.RamPressureLevel);
        Assert.Equal(PressureLevel.High, modified.RamPressureLevel);
        Assert.Equal(original.UsableRamNow, modified.UsableRamNow);
    }
}
