namespace Nexus.Models.Enums;

/// <summary>
/// Serialization format of the model weights file.
/// </summary>
public enum ModelFormat
{
    GGUF,
    SafeTensors,
    ONNX,
    OllamaManaged
}
