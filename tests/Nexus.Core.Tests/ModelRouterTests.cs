using Nexus.Core.Services;
using Nexus.Core.Config;
using Xunit;

namespace Nexus.Core.Tests;

public class ModelRouterTests
{
    private readonly ModelRouter _router;

    public ModelRouterTests()
    {
        var routing = new RoutingConfig
        {
            EntityExtraction = "local",
            InteractionSummary = "local",
            EntityResolution = "local",
            MemoryQueryResponse = "local",
            ComplexReasoning = "cloud",
            CodeGeneration = "cloud",
            Default = "local"
        };
        _router = new ModelRouter(routing);
    }

    [Theory]
    [InlineData(TaskType.EntityExtraction, "local")]
    [InlineData(TaskType.InteractionSummary, "local")]
    [InlineData(TaskType.EntityResolution, "local")]
    [InlineData(TaskType.MemoryQueryResponse, "local")]
    [InlineData(TaskType.ComplexReasoning, "cloud")]
    [InlineData(TaskType.CodeGeneration, "cloud")]
    [InlineData(TaskType.Default, "local")]
    public void Route_ShouldReturnCorrectProvider(TaskType task, string expectedProvider)
    {
        var result = _router.Route(task);
        Assert.Equal(expectedProvider, result);
    }

    [Fact]
    public void IsLocal_ForLocalTask_ShouldReturnTrue()
    {
        Assert.True(_router.IsLocal(TaskType.EntityExtraction));
    }

    [Fact]
    public void IsCloud_ForCloudTask_ShouldReturnTrue()
    {
        Assert.True(_router.IsCloud(TaskType.ComplexReasoning));
    }

    [Fact]
    public void IsLocal_ForCloudTask_ShouldReturnFalse()
    {
        Assert.False(_router.IsLocal(TaskType.CodeGeneration));
    }
}
