namespace Nexus.Models.Profiles;

using Nexus.Models.Enums;

/// <summary>
/// Captures the user's workload intent for model recommendation scoring.
/// </summary>
/// <param name="PrimaryIntent">Primary task the user wants the model to perform.</param>
/// <param name="SecondaryIntent">Optional secondary task intent, or <see langword="null"/>.</param>
/// <param name="InteractionPreference">Desired interaction style (low-latency, balanced, deep reasoning, batch).</param>
/// <param name="OutputPreference">Output optimization preference (speed, quality, stability).</param>
/// <param name="ExpectedPromptLength">Typical prompt length classification.</param>
/// <param name="ExpectedResponseLength">Typical response length classification.</param>
/// <param name="PrimaryLanguage">ISO code of the primary language for prompts and responses.</param>
/// <param name="SecondaryLanguages">Additional ISO language codes the model should support.</param>
/// <param name="MultilingualRequirement">Degree of multilingual capability required.</param>
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
