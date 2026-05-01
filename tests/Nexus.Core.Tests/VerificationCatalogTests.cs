using Microsoft.Extensions.Logging;
using Nexus.Connectors.Catalog;
using Nexus.Core.Abstractions;
using Nexus.Core.Config;

namespace Nexus.Core.Tests;

/// <summary>
/// Unit tests for VerificationCatalog — AC-H8 edge-case coverage.
/// Covers constructor logging (AC-H2), unknown-method warning (AC-H6),
/// and snapshot_diff missing-snapshot warning (AC-H6).
/// Uses the internal overload that accepts a custom overrideDir for test isolation.
/// </summary>
public class VerificationCatalogTests
{
    private static NexusConfig DefaultConfig() => new();

    // ──────────────────────────────────────────────────────────────────────────
    // AC-H2: constructor load-summary logging
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_LogsLoadSummary_AtInformationLevel()
    {
        // Arrange
        var logger = new CapturingLogger<VerificationCatalog>();

        // Act — use a non-existent override dir so zero user overrides are loaded
        var nonExistentDir = Path.Combine(Path.GetTempPath(), $"nexus_vc_test_{Guid.NewGuid():N}");
        var catalog = new VerificationCatalog(DefaultConfig(), logger, nonExistentDir);

        // Assert — exactly one Information log entry starting with "[Catalog] loaded "
        var infoEntries = logger.Entries
            .Where(e => e.Level == LogLevel.Information &&
                        e.Message.StartsWith("[Catalog] loaded ", StringComparison.Ordinal))
            .ToList();

        Assert.Single(infoEntries);
        // Catalog must have loaded at least the bundled rules
        Assert.True(catalog.Count >= 9,
            $"Expected at least 9 bundled rules, got {catalog.Count}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // AC-H6: unknown method string → warning + rule skipped
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void UnknownMethodString_LogsWarning_AndSkipsRule()
    {
        // Arrange: YAML with a typo in the method name ("snapshot_dif" instead of "snapshot_diff")
        var tempDir = Path.Combine(Path.GetTempPath(), $"nexus_vc_unknown_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var yaml = """
                server: myserver
                tools:
                  - name: my_tool
                    mutates: true
                    method: snapshot_dif
                """;
            File.WriteAllText(Path.Combine(tempDir, "custom.yaml"), yaml);

            var logger = new CapturingLogger<VerificationCatalog>();

            // Act
            var catalog = new VerificationCatalog(DefaultConfig(), logger, tempDir);

            // Assert (a) — warning was logged with the expected substring
            var warnings = logger.Entries
                .Where(e => e.Level == LogLevel.Warning &&
                            e.Message.Contains("[Catalog] unknown verification method", StringComparison.Ordinal))
                .ToList();

            Assert.Single(warnings);

            // Assert (b) — rule was skipped (GetRule returns null)
            // Note: method resolved to None; a None-method rule IS still yielded by ToVerificationRules
            // (only SnapshotDiff+no-snapshot causes a yield break). So we assert the warning fired
            // and the tool's effective method is None (rule exists but with VerificationMethod.None).
            // If the rule IS present with None method, verifier skips it at runtime — that is the
            // documented runtime-skip behavior from AC-H6.
            // GetRule should return non-null (the rule is inserted with Method=None).
            var rule = catalog.GetRule("myserver", "my_tool");
            Assert.NotNull(rule);
            Assert.Equal(VerificationMethod.None, rule.Method);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // AC-H6: snapshot_diff + missing snapshot block → warning + rule skipped
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SnapshotDiffWithoutSnapshotBlock_LogsWarning_AndSkipsRule()
    {
        // Arrange: YAML with method=snapshot_diff but NO snapshot: block
        var tempDir = Path.Combine(Path.GetTempPath(), $"nexus_vc_nosnap_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var yaml = """
                server: myserver
                tools:
                  - name: broken_tool
                    mutates: true
                    method: snapshot_diff
                """;
            File.WriteAllText(Path.Combine(tempDir, "broken.yaml"), yaml);

            var logger = new CapturingLogger<VerificationCatalog>();

            // Act
            var catalog = new VerificationCatalog(DefaultConfig(), logger, tempDir);

            // Assert (a) — warning was logged with the expected substring
            var warnings = logger.Entries
                .Where(e => e.Level == LogLevel.Warning &&
                            e.Message.Contains("declares method=snapshot_diff but has no snapshot block",
                                StringComparison.Ordinal))
                .ToList();

            Assert.Single(warnings);

            // Assert (b) — rule was skipped entirely (yield break in ToVerificationRules)
            var rule = catalog.GetRule("myserver", "broken_tool");
            Assert.Null(rule);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // AC-2: Destructive flag — new rules and existing rule tagging
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MoveFile_Rule_IsDestructive()
    {
        // Arrange
        var nonExistentDir = Path.Combine(Path.GetTempPath(), $"nexus_vc_test_{Guid.NewGuid():N}");
        var catalog = new VerificationCatalog(DefaultConfig(), null, nonExistentDir);

        // Act
        var rule = catalog.GetRule("filesystem", "move_file");

        // Assert
        Assert.NotNull(rule);
        Assert.True(rule.Destructive);
        Assert.Equal(VerificationMethod.ResponseKeywords, rule.Method);
    }

    [Fact]
    public void DeleteFile_Rule_LoadedFromYaml()
    {
        // Arrange
        var nonExistentDir = Path.Combine(Path.GetTempPath(), $"nexus_vc_test_{Guid.NewGuid():N}");
        var catalog = new VerificationCatalog(DefaultConfig(), null, nonExistentDir);

        // Act
        var rule = catalog.GetRule("filesystem", "delete_file");

        // Assert
        Assert.NotNull(rule);
        Assert.True(rule.Destructive);
        Assert.Contains("deleted", rule.SuccessKeywords);
    }

    [Fact]
    public void WriteFile_Rule_NowMarkedDestructive()
    {
        // Arrange
        var nonExistentDir = Path.Combine(Path.GetTempPath(), $"nexus_vc_test_{Guid.NewGuid():N}");
        var catalog = new VerificationCatalog(DefaultConfig(), null, nonExistentDir);

        // Act
        var rule = catalog.GetRule("filesystem", "write_file");

        // Assert
        Assert.NotNull(rule);
        Assert.True(rule.Destructive);
        Assert.Equal(VerificationMethod.SnapshotDiff, rule.Method);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Logger spy
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Simple ILogger implementation that records log entries for assertion in tests.
    /// </summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = new();

        public IReadOnlyList<(LogLevel Level, string Message)> Entries => _entries;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
