namespace Nexus.Models.Enums;

/// <summary>
/// Serialization format of the model weights file.
/// </summary>
public enum ModelFormat
{
    /// <summary>GGML Universal Format for llama.cpp-compatible models.</summary>
    GGUF,
    /// <summary>Hugging Face SafeTensors serialization format.</summary>
    SafeTensors,
    /// <summary>Open Neural Network Exchange format.</summary>
    ONNX,
    /// <summary>Ollama-managed model blob format.</summary>
    OllamaManaged
}
