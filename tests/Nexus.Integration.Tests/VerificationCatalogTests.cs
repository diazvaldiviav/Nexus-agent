using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Connectors.Catalog;
using Nexus.Core.Abstractions;
using Nexus.Core.Config;

namespace Nexus.Integration.Tests;

/// <summary>
/// Tests for VerificationCatalog (AC-6): bundled YAML loading, rule lookup,
/// user overrides, and malformed-YAML resilience.
/// </summary>
public class VerificationCatalogTests
{
    private static NexusConfig DefaultConfig() => new();

    // ── Bundled rule loading ──────────────────────────────────────────────────

    [Fact]
    public void Constructor_LoadsBundledRules_RuleCountAtLeastNine()
    {
        // Arrange + Act
        var catalog = new VerificationCatalog(DefaultConfig(), NullLogger<VerificationCatalog>.Instance);

        // Assert — filesystem.yaml alone defines 9 tools
        Assert.True(catalog.Count >= 9,
            $"Expected at least 9 bundled rules, got {catalog.Count}");
    }

    [Fact]
    public void GetRule_FilesystemWriteFile_ReturnsSnapshotDiff()
    {
        var catalog = new VerificationCatalog(DefaultConfig(), NullLogger<VerificationCatalog>.Instance);

        var rule = catalog.GetRule("filesystem", "write_file");

        Assert.NotNull(rule);
        Assert.Equal(VerificationMethod.SnapshotDiff, rule.Method);
        Assert.True(rule.Mutates);
        Assert.NotNull(rule.Snapshot);
        Assert.Equal("read_text_file", rule.Snapshot.Tool);
        Assert.True(rule.EmptyPostIsFailure);
    }

    [Fact]
    public void GetRule_FilesystemCreateDirectory_ReturnsResponseKeywords()
    {
        var catalog = new VerificationCatalog(DefaultConfig(), NullLogger<VerificationCatalog>.Instance);

        var rule = catalog.GetRule("filesystem", "create_directory");

        Assert.NotNull(rule);
        Assert.Equal(VerificationMethod.ResponseKeywords, rule.Method);
        Assert.True(rule.Mutates);
        Assert.Contains("created", rule.SuccessKeywords);
        Assert.Contains("error", rule.FailureKeywords);
    }

    [Fact]
    public void GetRule_FilesystemReadTextFile_ReturnsMutatesFalse()
    {
        var catalog = new VerificationCatalog(DefaultConfig(), NullLogger<VerificationCatalog>.Instance);

        var rule = catalog.GetRule("filesystem", "read_text_file");

        Assert.NotNull(rule);
        Assert.False(rule.Mutates);
    }

    [Fact]
    public void GetRule_UnknownTool_ReturnsNull()
    {
        var catalog = new VerificationCatalog(DefaultConfig(), NullLogger<VerificationCatalog>.Instance);

        var rule = catalog.GetRule("filesystem", "nonexistent_tool_xyz");

        Assert.Null(rule);
    }

    [Fact]
    public void GetRule_UnknownServer_ReturnsNull()
    {
        var catalog = new VerificationCatalog(DefaultConfig(), NullLogger<VerificationCatalog>.Instance);

        var rule = catalog.GetRule("unknown_server", "write_file");

        Assert.Null(rule);
    }

    [Fact]
    public void GetRule_IsCaseInsensitive()
    {
        var catalog = new VerificationCatalog(DefaultConfig(), NullLogger<VerificationCatalog>.Instance);

        var rule1 = catalog.GetRule("FILESYSTEM", "WRITE_FILE");
        var rule2 = catalog.GetRule("FileSystem", "Write_File");

        Assert.NotNull(rule1);
        Assert.NotNull(rule2);
        Assert.Equal(VerificationMethod.SnapshotDiff, rule1.Method);
        Assert.Equal(VerificationMethod.SnapshotDiff, rule2.Method);
    }

    // ── User overrides ────────────────────────────────────────────────────────

    [Fact]
    public void UserOverride_ReplacesBundledRule()
    {
        // Arrange: write a user YAML that overrides write_file with mutates:false
        var tempDir = Path.Combine(Path.GetTempPath(), $"nexus_catalog_override_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var overrideYaml = """
                server: filesystem
                tools:
                  - name: write_file
                    mutates: false
                """;
            File.WriteAllText(Path.Combine(tempDir, "override.yaml"), overrideYaml);

            var catalog = new VerificationCatalog(DefaultConfig(), NullLogger<VerificationCatalog>.Instance, tempDir);

            // Act
            var rule = catalog.GetRule("filesystem", "write_file");

            // Assert: user override wins — mutates is now false
            Assert.NotNull(rule);
            Assert.False(rule.Mutates);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void UserOverrideDir_DoesNotExist_LoadsSilently()
    {
        // Arrange: directory that definitely does not exist
        var nonExistentDir = Path.Combine(Path.GetTempPath(), $"nexus_catalog_nonexistent_{Guid.NewGuid():N}");

        // Act + Assert: no exception thrown, bundled rules still loaded
        var catalog = new VerificationCatalog(DefaultConfig(), NullLogger<VerificationCatalog>.Instance, nonExistentDir);
        Assert.True(catalog.Count >= 9);
    }

    [Fact]
    public void MalformedYaml_InUserDir_LogsWarningAndContinues()
    {
        // Arrange: user dir with one malformed + one valid YAML
        var tempDir = Path.Combine(Path.GetTempPath(), $"nexus_catalog_malformed_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Malformed YAML
            File.WriteAllText(Path.Combine(tempDir, "bad.yaml"), "{{{{: this is not valid yaml at all: ::::");

            // Valid extra rule
            var validYaml = """
                server: custom
                tools:
                  - name: my_tool
                    mutates: true
                    method: response_keywords
                    success_keywords: ["ok"]
                """;
            File.WriteAllText(Path.Combine(tempDir, "custom.yaml"), validYaml);

            var catalog = new VerificationCatalog(DefaultConfig(), NullLogger<VerificationCatalog>.Instance, tempDir);

            // Assert: still loaded (bundled + valid custom rule)
            Assert.True(catalog.Count >= 10,
                $"Expected at least 10 rules (9 bundled + 1 valid custom), got {catalog.Count}");

            var customRule = catalog.GetRule("custom", "my_tool");
            Assert.NotNull(customRule);
            Assert.Equal(VerificationMethod.ResponseKeywords, customRule.Method);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
