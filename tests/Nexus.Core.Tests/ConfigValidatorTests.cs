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

    // ── ValidateToolFilteringEnabled ───────────────────────────────────────

    [Fact]
    public void ValidateToolFilteringEnabled_EnabledWithModel_ReturnsNull()
    {
        var result = ConfigValidator.ValidateToolFilteringEnabled(true, "qwen3:14b");
        Assert.Null(result);
    }

    [Fact]
    public void ValidateToolFilteringEnabled_EnabledWithoutModel_ReturnsError()
    {
        var result = ConfigValidator.ValidateToolFilteringEnabled(true, null);
        Assert.NotNull(result);
        Assert.Contains("local model", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateToolFilteringEnabled_Disabled_ReturnsNull()
    {
        var result = ConfigValidator.ValidateToolFilteringEnabled(false, null);
        Assert.Null(result);
    }

    [Fact]
    public void Validate_ToolFilteringEnabledNoLocalModel_ReturnsError()
    {
        // Arrange
        var config = new NexusConfig();
        config.Mcp.ToolFilteringEnabled = true;
        config.Models.Local.Model = "";

        // Act
        var result = ConfigValidator.Validate(config);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotNull(result.GetError("Mcp.ToolFilteringEnabled"));
    }

    // ── ValidateToolPlanningEnabled ────────────────────────────────────────

    [Fact]
    public void ToolPlanningEnabled_RequiresLocalModel()
    {
        // Arrange: planning enabled but local provider/model are empty → error expected
        var result = ConfigValidator.ValidateToolPlanningEnabled(
            enabled: true,
            local: new ModelProviderConfig { Provider = "", Model = "" });

        Assert.NotNull(result);
        Assert.Contains("local provider", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolPlanningEnabled_AllowsDefaultConfig()
    {
        // Arrange: planning disabled (default) → no error even if local is unconfigured
        var result = ConfigValidator.ValidateToolPlanningEnabled(
            enabled: false,
            local: new ModelProviderConfig { Provider = "", Model = "" });

        Assert.Null(result);
    }

    // ── ValidateStepExecutionMaxAttempts ──────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(20)]
    [InlineData(21)]
    public void StepExecutionMaxAttempts_RangeValidated(int value)
    {
        var result = ConfigValidator.ValidateStepExecutionMaxAttempts(value);
        if (value < 1 || value > 20)
        {
            Assert.NotNull(result);
            Assert.Contains("1 and 20", result);
        }
        else
        {
            Assert.Null(result);
        }
    }

    [Fact]
    public void StepExecutionMaxAttempts_DefaultValue_IsFive()
    {
        var config = new NexusConfig();
        Assert.Equal(5, config.Mcp.StepExecutionMaxAttempts);
    }

    // ── ValidateToolPlanningTimeoutSeconds ─────────────────────────────────

    [Theory]
    [InlineData(4)]    // below min (5) → error
    [InlineData(5)]    // at min boundary → ok
    [InlineData(30)]   // in range → ok
    [InlineData(300)]  // at max boundary → ok
    [InlineData(301)]  // above max (300) → error
    public void ToolPlanningTimeoutSeconds_RangeValidated(int value)
    {
        var result = ConfigValidator.ValidateToolPlanningTimeoutSeconds(value);

        if (value == 4 || value == 301)
        {
            Assert.NotNull(result);   // out-of-range values must produce an error
            // Error message must mention the valid range bounds (5..300)
            Assert.Contains("5", result);
            Assert.Contains("300", result);
        }
        else
        {
            Assert.Null(result);      // in-range values must produce no error
        }
    }

    // ── Phase 9: PlannerContextMaxBytes ──────────────────────────────────────

    [Theory]
    [InlineData(100)]    // below min (200) → error
    [InlineData(200)]    // at min boundary → ok
    [InlineData(1500)]   // default, in range → ok
    [InlineData(16000)]  // at max boundary → ok
    [InlineData(16001)]  // above max → error
    public void PlannerContextMaxBytes_RangeValidated(int value)
    {
        var result = ConfigValidator.ValidatePlannerContextMaxBytes(value);

        if (value == 100 || value == 16001)
        {
            Assert.NotNull(result);
            Assert.Contains("200", result);
            Assert.Contains("16000", result);
        }
        else
        {
            Assert.Null(result);
        }
    }

    // ── Phase 9: PlannerContextMaxRecentTurns ────────────────────────────────

    [Theory]
    [InlineData(0)]   // below min (1) → error
    [InlineData(1)]   // at min boundary → ok
    [InlineData(4)]   // default → ok
    [InlineData(20)]  // at max boundary → ok
    [InlineData(21)]  // above max → error
    public void PlannerContextMaxRecentTurns_RangeValidated(int value)
    {
        var result = ConfigValidator.ValidatePlannerContextMaxRecentTurns(value);

        if (value == 0 || value == 21)
        {
            Assert.NotNull(result);
            Assert.Contains("1", result);
            Assert.Contains("20", result);
        }
        else
        {
            Assert.Null(result);
        }
    }

    // ── Phase 9: PlannerContextMaxBytesPerTurn ───────────────────────────────

    [Theory]
    [InlineData(79)]    // below min (80) → error
    [InlineData(80)]    // at min boundary → ok
    [InlineData(280)]   // default → ok
    [InlineData(4000)]  // at max boundary → ok
    [InlineData(4001)]  // above max → error
    public void PlannerContextMaxBytesPerTurn_RangeValidated(int value)
    {
        var result = ConfigValidator.ValidatePlannerContextMaxBytesPerTurn(value);

        if (value == 79 || value == 4001)
        {
            Assert.NotNull(result);
            Assert.Contains("80", result);
            Assert.Contains("4000", result);
        }
        else
        {
            Assert.Null(result);
        }
    }

    // ── Phase 9: PlannerContextEnabled default ───────────────────────────────

    [Fact]
    public void PlannerContextEnabled_DefaultValue_IsTrue()
    {
        var config = new NexusConfig();
        Assert.True(config.Mcp.PlannerContextEnabled);
    }

    // ── Phase 9 Wave C: VerificationSnapshotTimeoutSeconds ──────────────────

    [Theory]
    [InlineData(0)]   // below min (1) → error
    [InlineData(1)]   // at min boundary → ok
    [InlineData(10)]  // default → ok
    [InlineData(60)]  // at max boundary → ok
    [InlineData(61)]  // above max → error
    public void VerificationSnapshotTimeoutSeconds_RangeValidated(int value)
    {
        var result = ConfigValidator.ValidateVerificationSnapshotTimeoutSeconds(value);

        if (value == 0 || value == 61)
        {
            Assert.NotNull(result);
            Assert.Contains("1", result);
            Assert.Contains("60", result);
        }
        else
        {
            Assert.Null(result);
        }
    }

    [Fact]
    public void VerificationSnapshotTimeoutSeconds_DefaultValue_IsTen()
    {
        var config = new NexusConfig();
        Assert.Equal(10, config.Mcp.VerificationSnapshotTimeoutSeconds);
    }

    [Fact]
    public void ToolVerificationEnabled_DefaultValue_IsTrue()
    {
        var config = new NexusConfig();
        Assert.True(config.Mcp.ToolVerificationEnabled);
    }

    [Fact]
    public void Validate_VerificationSnapshotTimeoutOutOfRange_ReturnsError()
    {
        var config = new NexusConfig();
        config.Mcp.VerificationSnapshotTimeoutSeconds = 0;

        var result = ConfigValidator.Validate(config);

        Assert.False(result.IsValid);
        Assert.NotNull(result.GetError("Mcp.VerificationSnapshotTimeoutSeconds"));
    }

    // ── AC-7: PathValidatorStrictDistance ────────────────────────────────

    [Theory]
    [InlineData(49)]    // below min (50) → error
    [InlineData(50)]    // at min boundary → ok
    [InlineData(90)]    // default → ok
    [InlineData(100)]   // at max boundary → ok
    [InlineData(101)]   // above max (100) → error
    public void PathValidatorStrictDistance_RangeValidated(int value)
    {
        var result = ConfigValidator.ValidatePathValidatorStrictDistance(value);

        if (value == 49 || value == 101)
        {
            Assert.NotNull(result);
            Assert.Contains("50", result);
            Assert.Contains("100", result);
        }
        else
        {
            Assert.Null(result);
        }
    }

    [Fact]
    public void PathValidatorStrictDistance_DefaultValue_IsEighty()
    {
        var config = new NexusConfig();
        Assert.Equal(80, config.Mcp.PathValidatorStrictDistance);
    }

    // ── AC-1: PlannerHeuristicMinLength ───────────────────────────────────────

    [Theory]
    [InlineData(0)]    // below min (1) → error
    [InlineData(1)]    // at min boundary → ok
    [InlineData(16)]   // default → ok
    [InlineData(200)]  // at max boundary → ok
    [InlineData(201)]  // above max (200) → error
    public void PlannerHeuristicMinLength_RangeValidated(int value)
    {
        var result = ConfigValidator.ValidatePlannerHeuristicMinLength(value);

        if (value == 0 || value == 201)
        {
            Assert.NotNull(result);
            Assert.Contains("1", result);
            Assert.Contains("200", result);
        }
        else
        {
            Assert.Null(result);
        }
    }

    // ── AC-3: PermissionConfig ────────────────────────────────────────────────

    [Fact]
    public void PermissionEnabled_DefaultTrue()
    {
        var config = new NexusConfig();
        Assert.True(config.Permission.Enabled);
    }

    [Fact]
    public void Permission_Enabled_ParsedFromYaml_NotDefaulted()
    {
        // Arrange — parse YAML that explicitly sets enabled: false
        var yaml = "permission:\n  enabled: false\n";
        var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
            .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        // Act
        var config = deserializer.Deserialize<NexusConfig>(yaml);

        // Assert
        Assert.NotNull(config);
        Assert.False(config.Permission.Enabled);
    }

    [Theory]
    [InlineData("allow",   null)]
    [InlineData("ask",     null)]
    [InlineData("deny",    null)]
    [InlineData("ALLOW",   null)]
    [InlineData("garbage", "PermissionToolRule.Action must be")]
    public void PermissionToolRule_Action_RejectsInvalidValues(string action, string? expectedErrorFragment)
    {
        var result = ConfigValidator.ValidatePermissionAction(action);

        if (expectedErrorFragment is null)
            Assert.Null(result);
        else
        {
            Assert.NotNull(result);
            Assert.Contains(expectedErrorFragment, result, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("allow",   null)]
    [InlineData("ask",     null)]
    [InlineData("deny",    null)]
    [InlineData("DENY",    null)]
    [InlineData("bad",     "PermissionToolRule.Action must be")]
    public void PermissionToolRule_Patterns_RejectsInvalidValues(string action, string? expectedErrorFragment)
    {
        // The validator uses the same ValidatePermissionAction for pattern dict values.
        var result = ConfigValidator.ValidatePermissionAction(action);

        if (expectedErrorFragment is null)
            Assert.Null(result);
        else
        {
            Assert.NotNull(result);
            Assert.Contains(expectedErrorFragment, result, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Validate_PermissionToolRule_InvalidAction_ReturnsError()
    {
        // Arrange
        var config = new NexusConfig();
        config.Permission.Tools["write_file"] = new PermissionToolRule { Action = "nope" };

        // Act
        var result = ConfigValidator.Validate(config);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotNull(result.GetError("Permission.Tools[write_file].Action"));
    }

    [Fact]
    public void Validate_PermissionToolRule_InvalidPatternAction_ReturnsError()
    {
        // Arrange
        var config = new NexusConfig();
        config.Permission.Tools["delete_file"] = new PermissionToolRule
        {
            Action   = "ask",
            Patterns = new Dictionary<string, string> { ["**/*.log"] = "invalid" }
        };

        // Act
        var result = ConfigValidator.Validate(config);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotNull(result.GetError("Permission.Tools[delete_file].Patterns[**/*.log]"));
    }

    // ── Layer 2 (Sprint 10 follow-up): Embedding fallback config ─────────────────

    [Fact]
    public void ToolPlannerEmbeddingFallbackEnabled_DefaultIsTrue()
    {
        var config = new NexusConfig();
        Assert.True(config.Mcp.ToolPlannerEmbeddingFallbackEnabled);
    }

    [Fact]
    public void ToolPlannerEmbeddingMatchThreshold_DefaultIs065()
    {
        var config = new NexusConfig();
        Assert.Equal(0.65f, config.Mcp.ToolPlannerEmbeddingMatchThreshold);
    }

    [Theory]
    [InlineData(0.39f)]   // below min (0.40) → error
    [InlineData(0.40f)]   // at min boundary → ok
    [InlineData(0.65f)]   // default → ok
    [InlineData(0.95f)]   // at max boundary → ok
    [InlineData(0.96f)]   // above max → error
    public void ToolPlannerEmbeddingMatchThreshold_RangeValidated(float value)
    {
        var error = ConfigValidator.ValidateToolPlannerEmbeddingMatchThreshold(value);

        if (value < 0.40f || value > 0.95f)
            Assert.NotNull(error);
        else
            Assert.Null(error);
    }

}
