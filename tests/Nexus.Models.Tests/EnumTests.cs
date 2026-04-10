using Nexus.Models.Enums;

namespace Nexus.Models.Tests;

public class EnumTests
{
    [Fact]
    public void ModelFormat_HasExpectedValues()
    {
        var values = Enum.GetValues<ModelFormat>();
        Assert.Equal(4, values.Length);
        Assert.True(Enum.IsDefined(ModelFormat.GGUF));
        Assert.True(Enum.IsDefined(ModelFormat.SafeTensors));
        Assert.True(Enum.IsDefined(ModelFormat.ONNX));
        Assert.True(Enum.IsDefined(ModelFormat.OllamaManaged));
    }

    [Fact]
    public void BackendRuntime_HasExpectedValues()
    {
        var values = Enum.GetValues<BackendRuntime>();
        Assert.Equal(3, values.Length);
        Assert.True(Enum.IsDefined(BackendRuntime.LlamaCpp));
        Assert.True(Enum.IsDefined(BackendRuntime.OllamaRuntime));
        Assert.True(Enum.IsDefined(BackendRuntime.OnnxRuntime));
    }

    [Fact]
    public void ModelTaskFit_HasExpectedValues()
    {
        var values = Enum.GetValues<ModelTaskFit>();
        Assert.Equal(3, values.Length);
        Assert.True(Enum.IsDefined(ModelTaskFit.Chat));
        Assert.True(Enum.IsDefined(ModelTaskFit.Reasoning));
        Assert.True(Enum.IsDefined(ModelTaskFit.Coding));
    }

    [Fact]
    public void CpuCostClass_HasExpectedValues()
    {
        var values = Enum.GetValues<CpuCostClass>();
        Assert.Equal(4, values.Length);
        Assert.True(Enum.IsDefined(CpuCostClass.Low));
        Assert.True(Enum.IsDefined(CpuCostClass.Medium));
        Assert.True(Enum.IsDefined(CpuCostClass.High));
        Assert.True(Enum.IsDefined(CpuCostClass.VeryHigh));
    }

    [Fact]
    public void GpuCostClass_HasExpectedValues()
    {
        var values = Enum.GetValues<GpuCostClass>();
        Assert.Equal(4, values.Length);
        Assert.True(Enum.IsDefined(GpuCostClass.None));
        Assert.True(Enum.IsDefined(GpuCostClass.Low));
        Assert.True(Enum.IsDefined(GpuCostClass.Medium));
        Assert.True(Enum.IsDefined(GpuCostClass.High));
    }

    [Fact]
    public void InferenceSpeedClass_HasExpectedValues()
    {
        var values = Enum.GetValues<InferenceSpeedClass>();
        Assert.Equal(4, values.Length);
        Assert.True(Enum.IsDefined(InferenceSpeedClass.Fast));
        Assert.True(Enum.IsDefined(InferenceSpeedClass.Moderate));
        Assert.True(Enum.IsDefined(InferenceSpeedClass.Slow));
        Assert.True(Enum.IsDefined(InferenceSpeedClass.VerySlow));
    }

    [Fact]
    public void QualityTier_HasExpectedValues()
    {
        var values = Enum.GetValues<QualityTier>();
        Assert.Equal(4, values.Length);
        Assert.True(Enum.IsDefined(QualityTier.Basic));
        Assert.True(Enum.IsDefined(QualityTier.Good));
        Assert.True(Enum.IsDefined(QualityTier.Strong));
        Assert.True(Enum.IsDefined(QualityTier.Premium));
    }

    [Fact]
    public void InteractionPreference_HasExpectedValues()
    {
        var values = Enum.GetValues<InteractionPreference>();
        Assert.Equal(4, values.Length);
        Assert.True(Enum.IsDefined(InteractionPreference.LowLatency));
        Assert.True(Enum.IsDefined(InteractionPreference.Balanced));
        Assert.True(Enum.IsDefined(InteractionPreference.DeepReasoning));
        Assert.True(Enum.IsDefined(InteractionPreference.BatchProcessing));
    }

    [Fact]
    public void OutputPreference_HasExpectedValues()
    {
        var values = Enum.GetValues<OutputPreference>();
        Assert.Equal(4, values.Length);
        Assert.True(Enum.IsDefined(OutputPreference.MaxSpeed));
        Assert.True(Enum.IsDefined(OutputPreference.Balanced));
        Assert.True(Enum.IsDefined(OutputPreference.MaxQuality));
        Assert.True(Enum.IsDefined(OutputPreference.MaxStability));
    }

    [Fact]
    public void PromptLength_HasExpectedValues()
    {
        var values = Enum.GetValues<PromptLength>();
        Assert.Equal(4, values.Length);
        Assert.True(Enum.IsDefined(PromptLength.Short));
        Assert.True(Enum.IsDefined(PromptLength.Medium));
        Assert.True(Enum.IsDefined(PromptLength.Long));
        Assert.True(Enum.IsDefined(PromptLength.VeryLong));
    }

    [Fact]
    public void ResponseLength_HasExpectedValues()
    {
        var values = Enum.GetValues<ResponseLength>();
        Assert.Equal(4, values.Length);
        Assert.True(Enum.IsDefined(ResponseLength.Short));
        Assert.True(Enum.IsDefined(ResponseLength.Medium));
        Assert.True(Enum.IsDefined(ResponseLength.Long));
        Assert.True(Enum.IsDefined(ResponseLength.VeryLong));
    }

    [Fact]
    public void MultilingualRequirement_HasExpectedValues()
    {
        var values = Enum.GetValues<MultilingualRequirement>();
        Assert.Equal(3, values.Length);
        Assert.True(Enum.IsDefined(MultilingualRequirement.None));
        Assert.True(Enum.IsDefined(MultilingualRequirement.Basic));
        Assert.True(Enum.IsDefined(MultilingualRequirement.Strong));
    }

    [Fact]
    public void DistributionSource_HasExpectedValues()
    {
        var values = Enum.GetValues<DistributionSource>();
        Assert.Equal(2, values.Length);
        Assert.True(Enum.IsDefined(DistributionSource.Ollama));
        Assert.True(Enum.IsDefined(DistributionSource.HuggingFace));
        Assert.Equal(1, (int)DistributionSource.Ollama);
        Assert.Equal(2, (int)DistributionSource.HuggingFace);
        Assert.True(typeof(DistributionSource).IsDefined(typeof(FlagsAttribute), inherit: false));
    }

    [Fact]
    public void InstallComplexity_HasExpectedValues()
    {
        var values = Enum.GetValues<InstallComplexity>();
        Assert.Equal(3, values.Length);
        Assert.True(Enum.IsDefined(InstallComplexity.Low));
        Assert.True(Enum.IsDefined(InstallComplexity.Medium));
        Assert.True(Enum.IsDefined(InstallComplexity.High));
    }

    [Fact]
    public void CompatibleArchitecture_HasExpectedValues()
    {
        var values = Enum.GetValues<CompatibleArchitecture>();
        Assert.Equal(2, values.Length);
        Assert.True(Enum.IsDefined(CompatibleArchitecture.x64));
        Assert.True(Enum.IsDefined(CompatibleArchitecture.ARM64));
    }
}
