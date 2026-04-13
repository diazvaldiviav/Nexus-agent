namespace Nexus.Models.Enums;

/// <summary>
/// Inference runtime backend required to execute the model.
/// </summary>
public enum BackendRuntime
{
    /// <summary>Native llama.cpp inference engine.</summary>
    LlamaCpp,
    /// <summary>Ollama-managed inference runtime.</summary>
    OllamaRuntime,
    /// <summary>ONNX Runtime for optimized cross-platform inference.</summary>
    OnnxRuntime
}
