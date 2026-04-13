using Nexus.Core.Config;

namespace Nexus.Core.Tests;

public class ConfigValidatorTests
{
    [Fact]
    public void ValidateDecayLambda_InRange_ReturnsNull()
    {
        var result = ConfigValidator.ValidateDecayLambda(0.05);
        Assert.Null(result);
    }

    [Fact]
    public void ValidateDecayLambda_BelowMin_ReturnsError()
    {
        var result = ConfigValidator.ValidateDecayLambda(0.0001);
        Assert.NotNull(result);
        Assert.Contains("0.001", result);
    }

    [Fact]
    public void ValidateDecayLambda_AboveMax_ReturnsError()
    {
        var result = ConfigValidator.ValidateDecayLambda(1.5);
        Assert.NotNull(result);
        Assert.Contains("1.0", result);
    }

    [Fact]
    public void ValidateDecayLambda_AtBoundaryMin_ReturnsNull()
    {
        var result = ConfigValidator.ValidateDecayLambda(0.001);
        Assert.Null(result);
    }

    [Fact]
    public void ValidateDecayLambda_AtBoundaryMax_ReturnsNull()
    {
        var result = ConfigValidator.ValidateDecayLambda(1.0);
        Assert.Null(result);
    }

    [Fact]
    public void ValidateLocalEndpoint_ValidHttp_ReturnsNull()
    {
        var result = ConfigValidator.ValidateLocalEndpoint("http://localhost:11434");
        Assert.Null(result);
    }

    [Fact]
    public void ValidateLocalEndpoint_ValidHttps_ReturnsNull()
    {
        var result = ConfigValidator.ValidateLocalEndpoint("https://api.example.com");
        Assert.Null(result);
    }

    [Fact]
    public void ValidateLocalEndpoint_Empty_ReturnsNull()
    {
        var result = ConfigValidator.ValidateLocalEndpoint("");
        Assert.Null(result);
    }

    [Fact]
    public void ValidateLocalEndpoint_Null_ReturnsNull()
    {
        var result = ConfigValidator.ValidateLocalEndpoint(null);
        Assert.Null(result);
    }

    [Fact]
    public void ValidateLocalEndpoint_NotUri_ReturnsError()
    {
        var result = ConfigValidator.ValidateLocalEndpoint("not-a-url");
        Assert.NotNull(result);
    }

    [Fact]
    public void ValidateSummarizationInterval_Valid_ReturnsNull()
    {
        var result = ConfigValidator.ValidateSummarizationInterval(10);
        Assert.Null(result);
    }

    [Fact]
    public void ValidateSummarizationInterval_Zero_ReturnsError()
    {
        var result = ConfigValidator.ValidateSummarizationInterval(0);
        Assert.NotNull(result);
        Assert.Contains("at least 1", result);
    }

    [Fact]
    public void ValidateRecentInteractionsFetchLimit_InRange_ReturnsNull()
    {
        var result = ConfigValidator.ValidateRecentInteractionsFetchLimit(5);
        Assert.Null(result);
    }

    [Fact]
    public void ValidateRecentInteractionsFetchLimit_AboveMax_ReturnsError()
    {
        var result = ConfigValidator.ValidateRecentInteractionsFetchLimit(51);
        Assert.NotNull(result);
        Assert.Contains("50", result);
    }

    [Fact]
    public void CheckApiKeyWarning_ProviderMissingKey_ReturnsWarning()
    {
        var result = ConfigValidator.CheckApiKeyWarning("anthropic", null, null, null);
        Assert.NotNull(result);
        Assert.Contains("anthropic", result);
        Assert.Contains("Cloud features", result);
    }

    [Fact]
    public void Validate_ValidConfig_IsValid()
    {
        var config = new NexusConfig();
        config.Models.Local.Endpoint = "http://localhost:11434";

        var result = ConfigValidator.Validate(config);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_InvalidConfig_ReturnsSpecificErrors()
    {
        // Arrange
        var config = new NexusConfig();
        config.Memory.RelevanceDecayLambda = 0.0;
        config.Models.Local.Endpoint = "not-a-url";
        config.Memory.SummarizationInterval = 0;
        config.Memory.RecentInteractionsFetchLimit = 100;

        // Act
        var result = ConfigValidator.Validate(config);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotNull(result.GetError("DecayLambda"));
        Assert.NotNull(result.GetError("LocalEndpoint"));
        Assert.NotNull(result.GetError("SummarizationInterval"));
        Assert.NotNull(result.GetError("RecentInteractionsFetchLimit"));
        Assert.Equal(4, result.Errors.Count);
    }

    // ── ValidateMaxToolCallIterations ──────────────────────────────────────

    [Fact]
    public void ValidateMaxToolCallIterations_InRange_ReturnsNull()
    {
        var result = ConfigValidator.ValidateMaxToolCallIterations(5);
        Assert.Null(result);
    }

    [Fact]
    public void ValidateMaxToolCallIterations_BelowMin_ReturnsError()
    {
        var result = ConfigValidator.ValidateMaxToolCallIterations(0);
        Assert.NotNull(result);
        Assert.Contains("1", result);
        Assert.Contains("20", result);
    }

    [Fact]
    public void ValidateMaxToolCallIterations_AboveMax_ReturnsError()
    {
        var result = ConfigValidator.ValidateMaxToolCallIterations(21);
        Assert.NotNull(result);
        Assert.Contains("20", result);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    public void ValidateMaxToolCallIterations_AtBoundaries_ReturnsNull(int value)
    {
        var result = ConfigValidator.ValidateMaxToolCallIterations(value);
        Assert.Null(result);
    }

    // ── ValidateToolCallTimeoutSeconds ────────────────────────────────────

    [Fact]
    public void ValidateToolCallTimeoutSeconds_InRange_ReturnsNull()
    {
        var result = ConfigValidator.ValidateToolCallTimeoutSeconds(30);
        Assert.Null(result);
    }

    [Fact]
    public void ValidateToolCallTimeoutSeconds_BelowMin_ReturnsError()
    {
        var result = ConfigValidator.ValidateToolCallTimeoutSeconds(0);
        Assert.NotNull(result);
        Assert.Contains("1", result);
        Assert.Contains("300", result);
    }

    [Fact]
    public void ValidateToolCallTimeoutSeconds_AboveMax_ReturnsError()
    {
        var result = ConfigValidator.ValidateToolCallTimeoutSeconds(301);
        Assert.NotNull(result);
        Assert.Contains("300", result);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(300)]
    public void ValidateToolCallTimeoutSeconds_AtBoundaries_ReturnsNull(int value)
    {
        var result = ConfigValidator.ValidateToolCallTimeoutSeconds(value);
        Assert.Null(result);
    }

    // ── ValidateMaxOutputLines ─────────────────────────────────────────────

    [Fact]
    public void ValidateMaxOutputLines_InRange_ReturnsNull()
    {
        var result = ConfigValidator.ValidateMaxOutputLines(200);
        Assert.Null(result);
    }

    [Fact]
    public void ValidateMaxOutputLines_BelowMin_ReturnsError()
    {
        var result = ConfigValidator.ValidateMaxOutputLines(0);
        Assert.NotNull(result);
        Assert.Contains("1", result);
        Assert.Contains("10000", result);
    }

    [Fact]
    public void ValidateMaxOutputLines_AboveMax_ReturnsError()
    {
        var result = ConfigValidator.ValidateMaxOutputLines(10001);
        Assert.NotNull(result);
        Assert.Contains("10000", result);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10000)]
    public void ValidateMaxOutputLines_AtBoundaries_ReturnsNull(int value)
    {
        var result = ConfigValidator.ValidateMaxOutputLines(value);
        Assert.Null(result);
    }

    // ── ValidateMaxOutputBytes ─────────────────────────────────────────────

    [Fact]
    public void ValidateMaxOutputBytes_InRange_ReturnsNull()
    {
        var result = ConfigValidator.ValidateMaxOutputBytes(32000);
        Assert.Null(result);
    }

    [Fact]
    public void ValidateMaxOutputBytes_BelowMin_ReturnsError()
    {
        var result = ConfigValidator.ValidateMaxOutputBytes(999);
        Assert.NotNull(result);
        Assert.Contains("1000", result);
        Assert.Contains("500000", result);
    }

    [Fact]
    public void ValidateMaxOutputBytes_AboveMax_ReturnsError()
    {
        var result = ConfigValidator.ValidateMaxOutputBytes(500001);
        Assert.NotNull(result);
        Assert.Contains("500000", result);
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(500000)]
    public void ValidateMaxOutputBytes_AtBoundaries_ReturnsNull(int value)
    {
        var result = ConfigValidator.ValidateMaxOutputBytes(value);
        Assert.Null(result);
    }

    // ── ValidateMcpServerEntry ─────────────────────────────────────────────

    [Fact]
    public void ValidateMcpServerEntry_ValidStdio_ReturnsNull()
    {
        var entry = new McpServerEntry { Name = "my-server", Transport = "stdio", Command = "npx" };
        var result = ConfigValidator.ValidateMcpServerEntry(entry);
        Assert.Null(result);
    }

    [Fact]
    public void ValidateMcpServerEntry_ValidSse_ReturnsNull()
    {
        var entry = new McpServerEntry { Name = "my-sse", Transport = "sse", Url = "http://localhost:8080/sse" };
        var result = ConfigValidator.ValidateMcpServerEntry(entry);
        Assert.Null(result);
    }

    [Fact]
    public void ValidateMcpServerEntry_EmptyName_ReturnsError()
    {
        var entry = new McpServerEntry { Name = "", Transport = "stdio", Command = "npx" };
        var result = ConfigValidator.ValidateMcpServerEntry(entry);
        Assert.NotNull(result);
        Assert.Contains("name is required", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateMcpServerEntry_InvalidTransport_ReturnsError()
    {
        var entry = new McpServerEntry { Name = "my-server", Transport = "grpc" };
        var result = ConfigValidator.ValidateMcpServerEntry(entry);
        Assert.NotNull(result);
        Assert.Contains("grpc", result);
    }

    [Fact]
    public void ValidateMcpServerEntry_StdioMissingCommand_ReturnsError()
    {
        var entry = new McpServerEntry { Name = "my-server", Transport = "stdio", Command = null };
        var result = ConfigValidator.ValidateMcpServerEntry(entry);
        Assert.NotNull(result);
        Assert.Contains("Command is required", result);
    }

    [Fact]
    public void ValidateMcpServerEntry_SseMissingUrl_ReturnsError()
    {
        var entry = new McpServerEntry { Name = "my-sse", Transport = "sse", Url = null };
        var result = ConfigValidator.ValidateMcpServerEntry(entry);
        Assert.NotNull(result);
        Assert.Contains("Url is required", result);
    }

    [Fact]
    public void ValidateMcpServerEntry_SseInvalidUrl_ReturnsError()
    {
        var entry = new McpServerEntry { Name = "my-sse", Transport = "sse", Url = "not-a-url" };
        var result = ConfigValidator.ValidateMcpServerEntry(entry);
        Assert.NotNull(result);
        Assert.Contains("valid HTTP or HTTPS URL", result);
    }

    // ── Validate() integration ─────────────────────────────────────────────

    [Fact]
    public void Validate_InvalidMcpConfig_ReturnsAllErrors()
    {
        // Arrange: all 4 MCP ints out of range + server with empty name
        var config = new NexusConfig();
        config.Mcp.MaxToolCallIterations = 0;
        config.Mcp.ToolCallTimeoutSeconds = 0;
        config.Mcp.MaxOutputLines = 0;
        config.Mcp.MaxOutputBytes = 0;
        config.Mcp.Servers.Add(new McpServerEntry { Name = "", Transport = "stdio" });

        // Act
        var result = ConfigValidator.Validate(config);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotNull(result.GetError("Mcp.MaxToolCallIterations"));
        Assert.NotNull(result.GetError("Mcp.ToolCallTimeoutSeconds"));
        Assert.NotNull(result.GetError("Mcp.MaxOutputLines"));
        Assert.NotNull(result.GetError("Mcp.MaxOutputBytes"));
        Assert.NotNull(result.GetError("Mcp.Servers[0]"));
        Assert.Equal(5, result.Errors.Count);
    }

    [Fact]
    public void Validate_TwoInvalidServers_ReturnsTwoIndexedErrors()
    {
        // Arrange: two bad servers — both missing required fields
        var config = new NexusConfig();
        config.Mcp.Servers.Add(new McpServerEntry { Name = "server-a", Transport = "stdio", Command = null });
        config.Mcp.Servers.Add(new McpServerEntry { Name = "server-b", Transport = "sse", Url = null });

        // Act
        var result = ConfigValidator.Validate(config);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotNull(result.GetError("Mcp.Servers[0]"));
        Assert.NotNull(result.GetError("Mcp.Servers[1]"));
    }

}
