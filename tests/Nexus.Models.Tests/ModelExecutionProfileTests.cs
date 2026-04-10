using Nexus.Models.Enums;
using Nexus.Models.Profiles;

namespace Nexus.Models.Tests;

public class ModelExecutionProfileTests
{
    private static ModelExecutionProfile CreateProfile() =>
        new(
            CompatibleArchitectures: new List<CompatibleArchitecture> { CompatibleArchitecture.x64, CompatibleArchitecture.ARM64 },
            EstimatedRamOnLoad: 4_294_967_296L,
            EstimatedRamOnInference: 6_442_450_944L,
            EstimatedVramOnFullOffload: 4_294_967_296L,
            EstimatedVramOnPartialOffload: 2_147_483_648L,
            CpuCostClass: CpuCostClass.Medium,
            GpuCostClass: GpuCostClass.Low,
            InferenceSpeedClass: InferenceSpeedClass.Moderate,
            QualityTier: QualityTier.Good,
            RequiredRuntime: BackendRuntime.LlamaCpp);

    [Fact]
    public void Construction_WithAllProperties_SetsValues()
    {
        // Arrange & Act
        var profile = CreateProfile();

        // Assert
        Assert.Equal(2, profile.CompatibleArchitectures.Count);
        Assert.Equal(4_294_967_296L, profile.EstimatedRamOnLoad);
        Assert.Equal(6_442_450_944L, profile.EstimatedRamOnInference);
        Assert.Equal(4_294_967_296L, profile.EstimatedVramOnFullOffload);
        Assert.Equal(2_147_483_648L, profile.EstimatedVramOnPartialOffload);
        Assert.Equal(CpuCostClass.Medium, profile.CpuCostClass);
        Assert.Equal(GpuCostClass.Low, profile.GpuCostClass);
        Assert.Equal(InferenceSpeedClass.Moderate, profile.InferenceSpeedClass);
        Assert.Equal(QualityTier.Good, profile.QualityTier);
        Assert.Equal(BackendRuntime.LlamaCpp, profile.RequiredRuntime);
    }

    [Fact]
    public void CompatibleArchitectures_IReadOnlyList_WorksCorrectly()
    {
        // Arrange
        var profile = CreateProfile();

        // Act & Assert
        Assert.IsAssignableFrom<IReadOnlyList<CompatibleArchitecture>>(profile.CompatibleArchitectures);
        Assert.Contains(CompatibleArchitecture.x64, profile.CompatibleArchitectures);
        Assert.Contains(CompatibleArchitecture.ARM64, profile.CompatibleArchitectures);
    }

    [Fact]
    public void RecordEquality_SameValues_AreEqual()
    {
        // Arrange — share the same list reference so record structural equality holds
        var architectures = new List<CompatibleArchitecture> { CompatibleArchitecture.x64, CompatibleArchitecture.ARM64 };
        var profile1 = new ModelExecutionProfile(
            CompatibleArchitectures: architectures,
            EstimatedRamOnLoad: 4_294_967_296L,
            EstimatedRamOnInference: 6_442_450_944L,
            EstimatedVramOnFullOffload: 4_294_967_296L,
            EstimatedVramOnPartialOffload: 2_147_483_648L,
            CpuCostClass: CpuCostClass.Medium,
            GpuCostClass: GpuCostClass.Low,
            InferenceSpeedClass: InferenceSpeedClass.Moderate,
            QualityTier: QualityTier.Good,
            RequiredRuntime: BackendRuntime.LlamaCpp);
        var profile2 = new ModelExecutionProfile(
            CompatibleArchitectures: architectures,
            EstimatedRamOnLoad: 4_294_967_296L,
            EstimatedRamOnInference: 6_442_450_944L,
            EstimatedVramOnFullOffload: 4_294_967_296L,
            EstimatedVramOnPartialOffload: 2_147_483_648L,
            CpuCostClass: CpuCostClass.Medium,
            GpuCostClass: GpuCostClass.Low,
            InferenceSpeedClass: InferenceSpeedClass.Moderate,
            QualityTier: QualityTier.Good,
            RequiredRuntime: BackendRuntime.LlamaCpp);

        // Assert
        Assert.Equal(profile1, profile2);
        Assert.True(profile1 == profile2);
    }

    [Fact]
    public void Immutability_WithExpression_CreatesNewInstance()
    {
        // Arrange
        var original = CreateProfile();

        // Act
        var modified = original with { QualityTier = QualityTier.Premium, InferenceSpeedClass = InferenceSpeedClass.Slow };

        // Assert
        Assert.Equal(QualityTier.Good, original.QualityTier);
        Assert.Equal(InferenceSpeedClass.Moderate, original.InferenceSpeedClass);
        Assert.Equal(QualityTier.Premium, modified.QualityTier);
        Assert.Equal(InferenceSpeedClass.Slow, modified.InferenceSpeedClass);
        Assert.NotSame(original, modified);
    }
}
