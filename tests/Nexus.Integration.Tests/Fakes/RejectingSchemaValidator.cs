using Nexus.Core.Abstractions;

namespace Nexus.Integration.Tests.Fakes;

public sealed class RejectingSchemaValidator : ISchemaValidator
{
    public SchemaValidationResult Validate(string toolName, Dictionary<string, object>? args)
        => SchemaValidationResult.Fail(["Rejected by test validator"]);
}
