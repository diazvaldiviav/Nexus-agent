using Nexus.Core.Abstractions;

namespace Nexus.Integration.Tests.Fakes;

public sealed class PassthroughSchemaValidator : ISchemaValidator
{
    public SchemaValidationResult Validate(string toolName, Dictionary<string, object>? args)
        => SchemaValidationResult.Ok(args);
}
