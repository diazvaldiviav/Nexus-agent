using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Connectors;
using Nexus.Connectors.Catalog;
using Nexus.Core.Abstractions;
using Nexus.Core.Config;
using Nexus.Integration.Tests.Fakes;

namespace Nexus.Integration.Tests;

/// <summary>
/// Unit tests for McpToolVerifier (AC-7): routing, ResponseKeywords,
/// ResponseShape, SnapshotDiff, and EmptyPostIsFailure short-circuit.
/// </summary>
public class McpToolVerifierTests
{
    private static NexusConfig DefaultConfig() => new();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static McpToolVerifier CreateVerifier(
        IVerificationCatalog catalog,
        IMcpClientManager? mcpClient = null,
        NexusConfig? config = null)
    {
        return new McpToolVerifier(
            catalog,
            mcpClient ?? new FakeMcpClientManager(),
            config ?? DefaultConfig(),
            NullLogger<McpToolVerifier>.Instance);
    }

    private static IVerificationCatalog CatalogWith(params VerificationRule[] rules)
        => new FakeVerificationCatalog(rules);

    // ── No rule / non-mutating ────────────────────────────────────────────────

    [Fact]
    public async Task VerifyAsync_NoRule_ReturnsNoRule()
    {
        var catalog = CatalogWith();
        var verifier = CreateVerifier(catalog);

        var outcome = await verifier.VerifyAsync("filesystem", "read_file", null, null, "content");

        Assert.True(outcome.IsVerified);
        Assert.False(outcome.RuleMatched);
    }

    [Fact]
    public async Task VerifyAsync_MutatesFalse_ReturnsNoRule()
    {
        var rule = new VerificationRule
        {
            Server = "filesystem", Tool = "read_file",
            Mutates = false,
            Method = VerificationMethod.ResponseKeywords,
            SuccessKeywords = new List<string> { "ok" }
        };
        var catalog = CatalogWith(rule);
        var verifier = CreateVerifier(catalog);

        var outcome = await verifier.VerifyAsync("filesystem", "read_file", null, null, "ok");

        // Non-mutating tools return NoRule regardless of method
        Assert.True(outcome.IsVerified);
        Assert.False(outcome.RuleMatched);
    }

    // ── ResponseKeywords ──────────────────────────────────────────────────────

    [Fact]
    public async Task VerifyAsync_ResponseKeywords_SuccessKeywordPresent_ReturnsVerified()
    {
        var rule = new VerificationRule
        {
            Server = "filesystem", Tool = "create_directory",
            Mutates = true,
            Method = VerificationMethod.ResponseKeywords,
            SuccessKeywords = new List<string> { "created", "exists" },
            FailureKeywords = new List<string> { "error", "denied" }
        };
        var catalog = CatalogWith(rule);
        var verifier = CreateVerifier(catalog);

        var outcome = await verifier.VerifyAsync("filesystem", "create_directory",
            null, null, "Directory created successfully.");

        Assert.True(outcome.IsVerified);
        Assert.True(outcome.RuleMatched);
    }

    [Fact]
    public async Task VerifyAsync_ResponseKeywords_FailureKeywordDetected_ReturnsFailed()
    {
        var rule = new VerificationRule
        {
            Server = "filesystem", Tool = "create_directory",
            Mutates = true,
            Method = VerificationMethod.ResponseKeywords,
            SuccessKeywords = new List<string> { "created" },
            FailureKeywords = new List<string> { "error", "denied" }
        };
        var catalog = CatalogWith(rule);
        var verifier = CreateVerifier(catalog);

        var outcome = await verifier.VerifyAsync("filesystem", "create_directory",
            null, null, "Permission denied: cannot create directory.");

        Assert.False(outcome.IsVerified);
        Assert.True(outcome.RuleMatched);
        Assert.Contains("denied", outcome.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyAsync_ResponseKeywords_NoSuccessKeywords_AlwaysVerified()
    {
        var rule = new VerificationRule
        {
            Server = "svc", Tool = "do_thing",
            Mutates = true,
            Method = VerificationMethod.ResponseKeywords,
            SuccessKeywords = new List<string>(),
            FailureKeywords = new List<string>()
        };
        var catalog = CatalogWith(rule);
        var verifier = CreateVerifier(catalog);

        var outcome = await verifier.VerifyAsync("svc", "do_thing", null, null, "anything at all");

        Assert.True(outcome.IsVerified);
        Assert.True(outcome.RuleMatched);
    }

    [Fact]
    public async Task VerifyAsync_ResponseKeywords_NoSuccessKeywordFound_ReturnsFailed()
    {
        var rule = new VerificationRule
        {
            Server = "svc", Tool = "do_thing",
            Mutates = true,
            Method = VerificationMethod.ResponseKeywords,
            SuccessKeywords = new List<string> { "done" },
            FailureKeywords = new List<string>()
        };
        var catalog = CatalogWith(rule);
        var verifier = CreateVerifier(catalog);

        var outcome = await verifier.VerifyAsync("svc", "do_thing", null, null, "nothing useful here");

        Assert.False(outcome.IsVerified);
        Assert.Contains("no success keyword", outcome.Reason, StringComparison.OrdinalIgnoreCase);
    }

    // ── SnapshotDiff — EmptyPostIsFailure ─────────────────────────────────────

    [Fact]
    public async Task VerifyAsync_SnapshotDiff_EmptyPostIsFailure_PostEmpty_ReturnsFailed()
    {
        var rule = new VerificationRule
        {
            Server = "filesystem", Tool = "write_file",
            Mutates = true,
            Method = VerificationMethod.SnapshotDiff,
            EmptyPostIsFailure = true,
            Snapshot = new SnapshotSpec
            {
                Tool = "read_text_file",
                Args = new Dictionary<string, string> { ["path"] = "$.path" },
                Compare = "not_equal"
            }
        };

        // Fake client returns empty string for the post-snapshot read
        var fakeClient = new FakeMcpClientManager
        {
            InvokeResult = ""  // empty post-snapshot
        };

        var catalog = CatalogWith(rule);
        var verifier = CreateVerifier(catalog, fakeClient);

        var args = new Dictionary<string, object> { ["path"] = "/tmp/test.txt" };
        var preSnapshot = new Dictionary<string, object> { ["content"] = "original content" };

        var outcome = await verifier.VerifyAsync("filesystem", "write_file",
            args, preSnapshot, "File written.");

        Assert.False(outcome.IsVerified);
        Assert.True(outcome.RuleMatched);
        Assert.Contains("empty", outcome.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyAsync_SnapshotDiff_ContentChanged_ReturnsVerified()
    {
        var rule = new VerificationRule
        {
            Server = "filesystem", Tool = "write_file",
            Mutates = true,
            Method = VerificationMethod.SnapshotDiff,
            EmptyPostIsFailure = true,
            Snapshot = new SnapshotSpec
            {
                Tool = "read_text_file",
                Args = new Dictionary<string, string> { ["path"] = "$.path" },
                Compare = "not_equal"
            }
        };

        var fakeClient = new FakeMcpClientManager
        {
            InvokeResult = "new file content after write"
        };

        var catalog = CatalogWith(rule);
        var verifier = CreateVerifier(catalog, fakeClient);

        var args = new Dictionary<string, object> { ["path"] = "/tmp/test.txt" };
        var preSnapshot = new Dictionary<string, object> { ["content"] = "original content" };

        var outcome = await verifier.VerifyAsync("filesystem", "write_file",
            args, preSnapshot, "File written.");

        Assert.True(outcome.IsVerified);
        Assert.True(outcome.RuleMatched);
    }

    // ── CapturePreSnapshotAsync ───────────────────────────────────────────────

    [Fact]
    public async Task CapturePreSnapshotAsync_NoRule_ReturnsNull()
    {
        var catalog = CatalogWith();
        var verifier = CreateVerifier(catalog);

        var result = await verifier.CapturePreSnapshotAsync("filesystem", "write_file", null);

        Assert.Null(result);
    }

    [Fact]
    public async Task CapturePreSnapshotAsync_SnapshotRule_ReturnsContent()
    {
        var rule = new VerificationRule
        {
            Server = "filesystem", Tool = "write_file",
            Mutates = true,
            Method = VerificationMethod.SnapshotDiff,
            Snapshot = new SnapshotSpec
            {
                Tool = "read_text_file",
                Args = new Dictionary<string, string> { ["path"] = "$.path" },
                Compare = "not_equal"
            }
        };

        var fakeClient = new FakeMcpClientManager { InvokeResult = "existing file content" };
        var catalog = CatalogWith(rule);
        var verifier = CreateVerifier(catalog, fakeClient);

        var args = new Dictionary<string, object> { ["path"] = "/tmp/test.txt" };

        var snapshot = await verifier.CapturePreSnapshotAsync("filesystem", "write_file", args);

        Assert.NotNull(snapshot);
        Assert.True(snapshot.ContainsKey("content"));
        Assert.Equal("existing file content", snapshot["content"].ToString());
    }
}

// ── Test double ───────────────────────────────────────────────────────────────

/// <summary>
/// In-memory IVerificationCatalog backed by a fixed list of rules.
/// </summary>
file sealed class FakeVerificationCatalog : IVerificationCatalog
{
    private readonly Dictionary<(string, string), VerificationRule> _rules;

    public FakeVerificationCatalog(IEnumerable<VerificationRule> rules)
    {
        _rules = rules.ToDictionary(
            r => (r.Server.ToLowerInvariant(), r.Tool.ToLowerInvariant()),
            r => r);
    }

    public int Count => _rules.Count;

    public VerificationRule? GetRule(string server, string tool) =>
        _rules.TryGetValue((server.ToLowerInvariant(), tool.ToLowerInvariant()), out var rule)
            ? rule : null;
}
