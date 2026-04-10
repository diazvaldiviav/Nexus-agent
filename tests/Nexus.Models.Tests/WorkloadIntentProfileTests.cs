using System.Text.Json;
using Nexus.Models.Enums;
using Nexus.Models.Profiles;

namespace Nexus.Models.Tests;

public class WorkloadIntentProfileTests
{
    private static readonly IReadOnlyList<string> SharedSecondaryLanguages =
        new List<string> { "es", "fr" };

    private static WorkloadIntentProfile CreateProfile() =>
        new(
            PrimaryIntent: ModelTaskFit.Coding,
            SecondaryIntent: ModelTaskFit.Reasoning,
            InteractionPreference: InteractionPreference.DeepReasoning,
            OutputPreference: OutputPreference.MaxQuality,
            ExpectedPromptLength: PromptLength.Long,
            ExpectedResponseLength: ResponseLength.Long,
            PrimaryLanguage: "en",
            SecondaryLanguages: SharedSecondaryLanguages,
            MultilingualRequirement: MultilingualRequirement.Strong);

    [Fact]
    public void Construction_WithAllProperties_SetsValues()
    {
        // Arrange & Act
        var profile = CreateProfile();

        // Assert
        Assert.Equal(ModelTaskFit.Coding, profile.PrimaryIntent);
        Assert.Equal(ModelTaskFit.Reasoning, profile.SecondaryIntent);
        Assert.Equal(InteractionPreference.DeepReasoning, profile.InteractionPreference);
        Assert.Equal(OutputPreference.MaxQuality, profile.OutputPreference);
        Assert.Equal(PromptLength.Long, profile.ExpectedPromptLength);
        Assert.Equal(ResponseLength.Long, profile.ExpectedResponseLength);
        Assert.Equal("en", profile.PrimaryLanguage);
        Assert.Equal(2, profile.SecondaryLanguages.Count);
        Assert.Equal(MultilingualRequirement.Strong, profile.MultilingualRequirement);
    }

    [Fact]
    public void Default_ReturnsBalancedChatProfile()
    {
        // Arrange & Act
        var profile = WorkloadIntentProfile.Default();

        // Assert
        Assert.Equal(ModelTaskFit.Chat, profile.PrimaryIntent);
        Assert.Null(profile.SecondaryIntent);
        Assert.Equal(InteractionPreference.Balanced, profile.InteractionPreference);
        Assert.Equal(OutputPreference.Balanced, profile.OutputPreference);
        Assert.Equal(PromptLength.Medium, profile.ExpectedPromptLength);
        Assert.Equal(ResponseLength.Medium, profile.ExpectedResponseLength);
        Assert.Equal("en", profile.PrimaryLanguage);
        Assert.Empty(profile.SecondaryLanguages);
        Assert.Equal(MultilingualRequirement.None, profile.MultilingualRequirement);
    }

    [Fact]
    public void Immutability_WithExpression_CreatesNewInstance()
    {
        // Arrange
        var original = CreateProfile();

        // Act
        var modified = original with { PrimaryIntent = ModelTaskFit.Chat };

        // Assert
        Assert.Equal(ModelTaskFit.Coding, original.PrimaryIntent);
        Assert.Equal(ModelTaskFit.Chat, modified.PrimaryIntent);
        Assert.NotSame(original, modified);
    }

    [Fact]
    public void RecordEquality_SameValues_AreEqual()
    {
        // Arrange — share the same list reference so record structural equality holds
        var profile1 = new WorkloadIntentProfile(
            PrimaryIntent: ModelTaskFit.Coding,
            SecondaryIntent: ModelTaskFit.Reasoning,
            InteractionPreference: InteractionPreference.DeepReasoning,
            OutputPreference: OutputPreference.MaxQuality,
            ExpectedPromptLength: PromptLength.Long,
            ExpectedResponseLength: ResponseLength.Long,
            PrimaryLanguage: "en",
            SecondaryLanguages: SharedSecondaryLanguages,
            MultilingualRequirement: MultilingualRequirement.Strong);
        var profile2 = new WorkloadIntentProfile(
            PrimaryIntent: ModelTaskFit.Coding,
            SecondaryIntent: ModelTaskFit.Reasoning,
            InteractionPreference: InteractionPreference.DeepReasoning,
            OutputPreference: OutputPreference.MaxQuality,
            ExpectedPromptLength: PromptLength.Long,
            ExpectedResponseLength: ResponseLength.Long,
            PrimaryLanguage: "en",
            SecondaryLanguages: SharedSecondaryLanguages,
            MultilingualRequirement: MultilingualRequirement.Strong);

        // Assert
        Assert.Equal(profile1, profile2);
        Assert.True(profile1 == profile2);
    }

    [Fact]
    public void SecondaryIntent_WhenNull_IsNull()
    {
        // Arrange & Act
        var profile = new WorkloadIntentProfile(
            PrimaryIntent: ModelTaskFit.Chat,
            SecondaryIntent: null,
            InteractionPreference: InteractionPreference.Balanced,
            OutputPreference: OutputPreference.Balanced,
            ExpectedPromptLength: PromptLength.Medium,
            ExpectedResponseLength: ResponseLength.Medium,
            PrimaryLanguage: "en",
            SecondaryLanguages: Array.Empty<string>(),
            MultilingualRequirement: MultilingualRequirement.None);

        // Assert
        Assert.Null(profile.SecondaryIntent);
    }

    [Fact]
    public void JsonRoundTrip_SerializeDeserialize_PreservesValues()
    {
        // Arrange
        var profile = CreateProfile();

        // Act
        var json = JsonSerializer.Serialize(profile);
        var deserialized = JsonSerializer.Deserialize<WorkloadIntentProfile>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(ModelTaskFit.Coding, deserialized.PrimaryIntent);
        Assert.Equal(ModelTaskFit.Reasoning, deserialized.SecondaryIntent);
        Assert.Equal(InteractionPreference.DeepReasoning, deserialized.InteractionPreference);
        Assert.Equal(OutputPreference.MaxQuality, deserialized.OutputPreference);
        Assert.Equal(PromptLength.Long, deserialized.ExpectedPromptLength);
        Assert.Equal(ResponseLength.Long, deserialized.ExpectedResponseLength);
        Assert.Equal("en", deserialized.PrimaryLanguage);
        Assert.Equal(2, deserialized.SecondaryLanguages.Count);
        Assert.Equal("es", deserialized.SecondaryLanguages[0]);
        Assert.Equal("fr", deserialized.SecondaryLanguages[1]);
        Assert.Equal(MultilingualRequirement.Strong, deserialized.MultilingualRequirement);
    }

    [Fact]
    public void Default_CalledMultipleTimes_ReturnsEqualProfiles()
    {
        // Arrange & Act
        var profile1 = WorkloadIntentProfile.Default();
        var profile2 = WorkloadIntentProfile.Default();

        // Assert
        Assert.Equal(profile1, profile2);
    }
}
