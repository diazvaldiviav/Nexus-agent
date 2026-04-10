namespace Nexus.Models.Enums;

/// <summary>
/// Inference runtime backend required to execute the model.
/// </summary>
public enum BackendRuntime
{
    LlamaCpp,
    OllamaRuntime,
    OnnxRuntime
}
