using Nexus.Core.Config;
using Nexus.Core.Services;

namespace Nexus.Core.Tests;

public class PlannerInvocationHeuristicTests
{
    // ── Test 1: Greeting_Returns_False ───────────────────────────────────────

    [Theory]
    [InlineData("hola")]
    [InlineData("hi")]
    [InlineData("hello")]
    [InlineData("gracias")]
    [InlineData("thanks")]
    [InlineData("ok")]
    [InlineData("como estas")]
    [InlineData("buenos dias")]
    [InlineData("?")]
    [InlineData("…")]
    public void Greeting_Returns_False(string greeting)
    {
        // Arrange
        var config = new NexusConfig();

        // Act
        var (shouldPlan, reason) = PlannerInvocationHeuristic.ShouldInvokePlanner(greeting, config);

        // Assert
        Assert.False(shouldPlan);
        Assert.True(reason is "below_min_length" or "chat_greeting",
            $"Expected reason 'below_min_length' or 'chat_greeting' but got '{reason}' for input '{greeting}'");
    }

    // ── Test 2: LongTask_Returns_True ────────────────────────────────────────

    [Fact]
    public void LongTask_Returns_True()
    {
        // Arrange
        const string input = "Please write a TODO app with three columns and a database backend";
        var config = new NexusConfig();

        // Act
        var (shouldPlan, reason) = PlannerInvocationHeuristic.ShouldInvokePlanner(input, config);

        // Assert
        Assert.True(shouldPlan);
        Assert.True(reason is "default_allow" or "imperative_verb",
            $"Expected 'default_allow' or 'imperative_verb' but got '{reason}'");
    }

    // ── Test 3: Path_Returns_True ────────────────────────────────────────────

    [Fact]
    public void Path_Returns_True()
    {
        // Arrange
        const string input = @"look at D:\Nexus\index.html";
        var config = new NexusConfig();

        // Act
        var (shouldPlan, reason) = PlannerInvocationHeuristic.ShouldInvokePlanner(input, config);

        // Assert
        Assert.True(shouldPlan);
        Assert.True(reason is "path_match" or "imperative_verb" or "file_extension",
            $"Expected a positive trigger reason but got '{reason}'");
    }

    // ── Test 4: Verb_Returns_True ─────────────────────────────────────────────

    [Fact]
    public void Verb_Returns_True()
    {
        // Arrange
        const string input = "crea un boton en la pagina";
        var config = new NexusConfig();

        // Act
        var (shouldPlan, reason) = PlannerInvocationHeuristic.ShouldInvokePlanner(input, config);

        // Assert
        Assert.True(shouldPlan);
        Assert.Equal("imperative_verb", reason);
    }

    // ── Test 5: MinLength_Boundary ───────────────────────────────────────────
    // Default PlannerHeuristicMinLength = 16. Rule: length < 16 → blocked.
    // length == 16 → NOT blocked by length (passes to greeting/verb checks).
    // length > 16 → NOT blocked by length.

    [Theory]
    [InlineData("x", false)]                   // length 1  — below min → blocked
    [InlineData("exactly16chars!!", true)]      // length 16 — AT boundary, NOT blocked by length
    [InlineData("this is seventeen!!", true)]   // length 19 — above min → NOT blocked by length
    public void MinLength_Boundary(string message, bool expectedLengthEligible)
    {
        // Arrange: use default config (PlannerHeuristicMinLength = 16)
        var config = new NexusConfig();
        Assert.Equal(16, config.Mcp.PlannerHeuristicMinLength);

        // Act
        var (shouldPlan, reason) = PlannerInvocationHeuristic.ShouldInvokePlanner(message, config);

        // Assert
        if (!expectedLengthEligible)
        {
            // Short messages are blocked at the length gate regardless of content
            Assert.False(shouldPlan);
            Assert.Equal("below_min_length", reason);
        }
        else
        {
            // Length-eligible messages must NOT be blocked by the length gate;
            // they may still be blocked by a greeting check or allowed by default.
            Assert.NotEqual("below_min_length", reason);
        }
    }
}
