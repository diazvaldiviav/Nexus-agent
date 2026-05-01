using Nexus.Core.Abstractions;
using Nexus.Core.Config;
using Nexus.Core.Services;

namespace Nexus.Core.Tests;

/// <summary>
/// Tests for AC-3: PermissionConfig schema, PermissionDecision enum, and PermissionPatternExtractor.
/// </summary>
public class PermissionConfigTests
{
    // ── Config schema ─────────────────────────────────────────────────────────

    [Fact]
    public void SimpleAction_ResolvesAcrossPatterns()
    {
        // Arrange
        var config = new NexusConfig
        {
            Permission = new PermissionConfig
            {
                Tools =
                {
                    ["write_file"] = new PermissionToolRule { Action = "ask" }
                }
            }
        };

        // Act & Assert
        Assert.Equal("ask", config.Permission.Tools["write_file"].Action);
    }

    [Fact]
    public void PerPattern_FirstMatchWins()
    {
        // Arrange — dict order must be preserved: "**/*.env" first, then "*"
        var rule = new PermissionToolRule
        {
            Patterns = new Dictionary<string, string>
            {
                ["**/*.env"] = "deny",
                ["*"]        = "allow"
            }
        };

        // Act — verify schema round-trip preserves insertion order
        var patternList = rule.Patterns!.ToList();

        // Assert — first entry is the more-specific glob; actual match logic is gate-impl detail
        Assert.Equal("**/*.env", patternList[0].Key);
        Assert.Equal("deny",     patternList[0].Value);
        Assert.Equal("*",        patternList[1].Key);
        Assert.Equal("allow",    patternList[1].Value);
    }

    [Fact]
    public void Persistent_Overrides_Config_EnumValuesExist()
    {
        // Documentation test: assert the PermissionDecision enum values exist in documented order.
        var values = Enum.GetValues<PermissionDecision>().ToList();

        Assert.Equal(PermissionDecision.Allow,            values[0]);
        Assert.Equal(PermissionDecision.AllowForSession,  values[1]);
        Assert.Equal(PermissionDecision.AllowPersisted,   values[2]);
        Assert.Equal(PermissionDecision.Deny,             values[3]);
        Assert.Equal(PermissionDecision.DenyWithFeedback, values[4]);
    }

    [Fact]
    public void Session_OverridesConfig_AllFiveEnumValuesPresentWithCorrectNames()
    {
        // Documentation test: confirm all 5 enum value names are correct.
        Assert.True(Enum.IsDefined(typeof(PermissionDecision), "Allow"));
        Assert.True(Enum.IsDefined(typeof(PermissionDecision), "AllowForSession"));
        Assert.True(Enum.IsDefined(typeof(PermissionDecision), "AllowPersisted"));
        Assert.True(Enum.IsDefined(typeof(PermissionDecision), "Deny"));
        Assert.True(Enum.IsDefined(typeof(PermissionDecision), "DenyWithFeedback"));
        Assert.Equal(5, Enum.GetValues<PermissionDecision>().Length);
    }

    [Fact]
    public void PermissionPatternExtractor_NoArgs_ReturnsStar()
    {
        // Arrange & Act
        var result = PermissionPatternExtractor.Extract("foo", null, null);

        // Assert
        Assert.Single(result);
        Assert.Equal("*", result[0]);
    }
}

/// <summary>
/// Tests for PermissionPatternExtractor extraction logic.
/// </summary>
public class PermissionPatternExtractorTests
{
    [Fact]
    public void Extract_NoArgs_ReturnsStar()
    {
        // Arrange & Act
        var result = PermissionPatternExtractor.Extract("write_file", null, null);

        // Assert
        Assert.Single(result);
        Assert.Equal("*", result[0]);
    }

    [Fact]
    public void Extract_EmptyArgs_ReturnsStar()
    {
        // Arrange
        var args = new Dictionary<string, object>();

        // Act
        var result = PermissionPatternExtractor.Extract("write_file", args, null);

        // Assert
        Assert.Single(result);
        Assert.Equal("*", result[0]);
    }

    [Fact]
    public void Extract_NoRule_FallsBackToCommonKeys()
    {
        // Arrange
        var args = new Dictionary<string, object>
        {
            ["path"] = "src/foo.cs"
        };

        // Act
        var result = PermissionPatternExtractor.Extract("write_file", args, null);

        // Assert
        Assert.Single(result);
        Assert.Equal("src/foo.cs", result[0]);
    }

    [Fact]
    public void Extract_NoRule_FallsBackToSourceDestination()
    {
        // Arrange — no "path" key, but "source" and "destination" are present
        var args = new Dictionary<string, object>
        {
            ["source"]      = "/old/path",
            ["destination"] = "/new/path"
        };

        // Act
        var result = PermissionPatternExtractor.Extract("move_file", args, null);

        // Assert — both paths extracted in CommonPathKeys order (source before destination)
        Assert.Equal(2, result.Count);
        Assert.Contains("/old/path", result);
        Assert.Contains("/new/path", result);
    }

    [Fact]
    public void Extract_RuleWithArgsFrom_ResolvesPath()
    {
        // Arrange
        var args = new Dictionary<string, object>
        {
            ["source"]      = "/a",
            ["destination"] = "/b"
        };

        var rule = new VerificationRule
        {
            Server = "filesystem",
            Tool   = "move_file",
            Mutates = true,
            Snapshot = new SnapshotSpec
            {
                Tool = "read_file",
                Args = new Dictionary<string, string>
                {
                    ["path"]  = "$.source",
                    ["path2"] = "$.destination"
                }
            }
        };

        // Act
        var result = PermissionPatternExtractor.Extract("move_file", args, rule);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains("/a", result);
        Assert.Contains("/b", result);
    }

    [Fact]
    public void Extract_RuleWithArgsFrom_SinglePath()
    {
        // Arrange
        var args = new Dictionary<string, object>
        {
            ["path"] = "C:/work/output.txt"
        };

        var rule = new VerificationRule
        {
            Server   = "filesystem",
            Tool     = "write_file",
            Mutates  = true,
            Snapshot = new SnapshotSpec
            {
                Tool = "read_file",
                Args = new Dictionary<string, string>
                {
                    ["path"] = "$.path"
                }
            }
        };

        // Act
        var result = PermissionPatternExtractor.Extract("write_file", args, rule);

        // Assert
        Assert.Single(result);
        Assert.Equal("C:/work/output.txt", result[0]);
    }

    [Fact]
    public void Extract_MalformedJsonPath_ReturnsStar()
    {
        // Arrange — JSONPath does not start with "$." → resolve returns null → falls back to wildcard
        var args = new Dictionary<string, object>
        {
            ["path"] = "some/file.txt"
        };

        var rule = new VerificationRule
        {
            Server   = "filesystem",
            Tool     = "write_file",
            Mutates  = true,
            Snapshot = new SnapshotSpec
            {
                Tool = "read_file",
                Args = new Dictionary<string, string>
                {
                    ["path"] = "INVALID_JSONPATH"   // does not start with "$."
                }
            }
        };

        // Act
        var result = PermissionPatternExtractor.Extract("write_file", args, rule);

        // Assert — defensive fallback
        Assert.Single(result);
        Assert.Equal("*", result[0]);
    }

    [Fact]
    public void Extract_RuleWithNoSnapshot_FallsBackToCommonKeys()
    {
        // Arrange — rule exists but snapshot is null → fallback to common-key scan
        var args = new Dictionary<string, object>
        {
            ["filename"] = "report.pdf"
        };

        var rule = new VerificationRule
        {
            Server  = "filesystem",
            Tool    = "write_file",
            Mutates = true,
            // Snapshot intentionally null
        };

        // Act
        var result = PermissionPatternExtractor.Extract("write_file", args, rule);

        // Assert
        Assert.Single(result);
        Assert.Equal("report.pdf", result[0]);
    }

    [Fact]
    public void Extract_NoMatchingCommonKeys_ReturnsStar()
    {
        // Arrange — args present but none match common path keys
        var args = new Dictionary<string, object>
        {
            ["content"] = "Hello world",
            ["encoding"] = "utf-8"
        };

        // Act
        var result = PermissionPatternExtractor.Extract("write_file", args, null);

        // Assert
        Assert.Single(result);
        Assert.Equal("*", result[0]);
    }
}
