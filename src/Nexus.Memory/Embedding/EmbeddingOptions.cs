namespace Nexus.Memory.Embedding;

public record EmbeddingOptions(string Endpoint, string Model, int Dimensions = 768);
