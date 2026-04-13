using Nexus.Hardware.Envelopes;
using Nexus.Hardware.States;

namespace Nexus.Hardware.Tests;

public class GpuEnvelopeTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var envelope = new GpuEnvelope(8_000_000_000L, 6_000_000_000L, PressureLevel.Low, 4_000_000_000L, true, true);

        Assert.Equal(8_000_000_000L, envelope.UsableLocalVramNow);
        Assert.Equal(6_000_000_000L, envelope.SafeGpuBudget);
        Assert.Equal(PressureLevel.Low, envelope.GpuPressureLevel);
        Assert.Equal(4_000_000_000L, envelope.GpuOffloadCapacity);
        Assert.True(envelope.CanFullOffload);
        Assert.True(envelope.CanPartialOffload);
    }

    [Fact]
    public void IsViable_AlwaysReturnsTrue()
    {
        var envelope = new GpuEnvelope(8_000_000_000L, 6_000_000_000L, PressureLevel.Low, 4_000_000_000L, true, true);

        Assert.True(envelope.IsViable());
    }

    [Fact]
    public void IsViable_WithZeroValues_StillReturnsTrue()
    {
        var envelope = new GpuEnvelope(0, 0, PressureLevel.None, 0, false, false);

        Assert.True(envelope.IsViable());
    }

    [Fact]
    public void NoGpu_ReturnsZeroedInstance()
    {
        var envelope = GpuEnvelope.NoGpu();

        Assert.Equal(0, envelope.UsableLocalVramNow);
        Assert.Equal(0, envelope.SafeGpuBudget);
        Assert.Equal(0, envelope.GpuOffloadCapacity);
        Assert.False(envelope.CanFullOffload);
        Assert.False(envelope.CanPartialOffload);
    }

    [Fact]
    public void NoGpu_PressureLevelIsNone()
    {
        var envelope = GpuEnvelope.NoGpu();

        Assert.Equal(PressureLevel.None, envelope.GpuPressureLevel);
    }

    [Fact]
    public void NoGpu_IsViable_ReturnsTrue()
    {
        var envelope = GpuEnvelope.NoGpu();

        Assert.True(envelope.IsViable());
    }

    [Fact]
    public void NoGpu_ClassifyGpu_ReturnsNone()
    {
        var envelope = GpuEnvelope.NoGpu();

        var state = HostStateClassifier.ClassifyGpu(envelope);

        Assert.Equal(GpuState.None, state);
    }

    [Fact]
    public void StructuralEquality_EqualValues_AreEqual()
    {
        var a = new GpuEnvelope(8_000_000_000L, 6_000_000_000L, PressureLevel.Low, 4_000_000_000L, true, false);
        var b = new GpuEnvelope(8_000_000_000L, 6_000_000_000L, PressureLevel.Low, 4_000_000_000L, true, false);

        Assert.Equal(a, b);
    }

    [Fact]
    public void StructuralEquality_DifferentValues_NotEqual()
    {
        var a = new GpuEnvelope(8_000_000_000L, 6_000_000_000L, PressureLevel.Low, 4_000_000_000L, true, false);
        var b = new GpuEnvelope(8_000_000_000L, 4_000_000_000L, PressureLevel.Low, 4_000_000_000L, true, false);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void WithExpression_CreatesModifiedCopy()
    {
        var original = new GpuEnvelope(8_000_000_000L, 6_000_000_000L, PressureLevel.Low, 4_000_000_000L, true, true);

        var modified = original with { CanFullOffload = false };

        Assert.True(original.CanFullOffload);
        Assert.False(modified.CanFullOffload);
        Assert.Equal(original.UsableLocalVramNow, modified.UsableLocalVramNow);
    }
}
