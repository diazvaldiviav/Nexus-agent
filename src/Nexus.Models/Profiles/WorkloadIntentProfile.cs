namespace Nexus.Models.Profiles;

using Nexus.Models.Enums;

/// <summary>
/// Captures the user's workload intent for model recommendation scoring.
/// </summary>
public record WorkloadIntentProfile(
    ModelTaskFit PrimaryIntent,
    ModelTaskFit? SecondaryIntent,
    InteractionPreference InteractionPreference,
    OutputPreference OutputPreference,
    PromptLength ExpectedPromptLength,
    ResponseLength ExpectedResponseLength,
    string PrimaryLanguage,
    IReadOnlyList<string> SecondaryLanguages,
    MultilingualRequirement MultilingualRequirement)
{
    /// <summary>
    /// Creates a balanced chat-oriented default profile.
    /// </summary>
    public static WorkloadIntentProfile Default() => new(
        PrimaryIntent: ModelTaskFit.Chat,
        SecondaryIntent: null,
        InteractionPreference: InteractionPreference.Balanced,
        OutputPreference: OutputPreference.Balanced,
        ExpectedPromptLength: PromptLength.Medium,
        ExpectedResponseLength: ResponseLength.Medium,
        PrimaryLanguage: "en",
        SecondaryLanguages: Array.Empty<string>(),
        MultilingualRequirement: MultilingualRequirement.None);
}
