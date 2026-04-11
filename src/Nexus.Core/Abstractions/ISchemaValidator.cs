namespace Nexus.Core.Abstractions;

public sealed class SchemaValidationResult
{
    public bool IsValid { get; private init; }
    public List<string> Errors { get; private init; } = [];
    public Dictionary<string, object>? CoercedArgs { get; private init; }

    public static SchemaValidationResult Ok(Dictionary<string, object>? args) =>
        new() { IsValid = true, CoercedArgs = args };

    public static SchemaValidationResult Fail(List<string> errors) =>
        new() { IsValid = false, Errors = errors };
}

public interface ISchemaValidator
{
    SchemaValidationResult Validate(string toolName, Dictionary<string, object>? args);
}
