using System.Text;
using Microsoft.Data.Sqlite;
using Nexus.CLI;
using Nexus.Core.Abstractions;
using Nexus.Core.Config;
using Nexus.Core.Services;
using Spectre.Console;
using Spectre.Console.Testing;

namespace Nexus.Integration.Tests;

/// <summary>
/// Integration tests for <see cref="CliPermissionGate"/>.
/// Each test redirects the permissions store to a temp file to avoid touching ~/.nexus/permissions.json.
/// </summary>
public sealed class CliPermissionGateTests : IDisposable
{
    private readonly string _tempDir;

    public CliPermissionGateTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "nexus_gate_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private string PermissionsFilePath => Path.Combine(_tempDir, "permissions.json");

    private static NexusConfig FullTierConfig() => new()
    {
        Models = new ModelsConfig
        {
            Local = new ModelProviderConfig { Model = "llama3:70b" }  // Full tier (≥ 30B)
        }
    };

    private static NexusConfig SmallModelConfig() => new()
    {
        Models = new ModelsConfig
        {
            Local = new ModelProviderConfig { Model = "qwen3:1.7b" }
        }
    };

    private PersistentPermissionStore CreateStore()
        => new(PermissionsFilePath);

    private static PermissionRequest MakeRequest(string toolName = "write_file", string serverName = "filesystem")
        => new(serverName, toolName,
            new Dictionary<string, object> { ["path"] = "D:/foo/bar.txt" }.AsReadOnly(),
            new[] { "D:/foo/bar.txt" },
            "destructive operation");

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// When config says "allow" for the tool, the gate returns Allow without ever writing
    /// to the permissions.json file.
    /// </summary>
    [Fact]
    public async Task Allow_ConfigRuleAllow_DoesNotWritePermissionsFile()
    {
        // Arrange
        var config = FullTierConfig();
        config.Permission.Tools["write_file"] = new PermissionToolRule { Action = "allow" };

        var store = CreateStore();
        var console = new TestConsole().Interactive();
        var gate = new CliPermissionGate(config, store, console);

        // Act
        var response = await gate.RequestAsync(MakeRequest(), CancellationToken.None);

        // Assert
        Assert.Equal(PermissionDecision.Allow, response.Decision);
        Assert.False(File.Exists(PermissionsFilePath),
            "permissions.json must NOT be written when config short-circuits to Allow");
    }

    /// <summary>
    /// After user selects "Allow for session", a second call with the same tool+pattern
    /// returns AllowForSession without showing the prompt again (session cache hit).
    /// </summary>
    [Fact]
    public async Task AllowForSession_SecondCall_ReturnsCachedWithoutPrompt()
    {
        // Arrange — full-tier model so session allowances are enabled
        var config = FullTierConfig();
        var store = CreateStore();

        // First call: navigate to "[s] Allow for session" (index 1 in full-tier list) and press Enter
        var console = new TestConsole().Interactive();
        console.Input.PushKey(ConsoleKey.DownArrow);  // move to [s]
        console.Input.PushKey(ConsoleKey.Enter);       // confirm

        var gate = new CliPermissionGate(config, store, console);
        var request = MakeRequest();

        // Act — first call
        var first = await gate.RequestAsync(request, CancellationToken.None);
        Assert.Equal(PermissionDecision.AllowForSession, first.Decision);

        // Act — second call (same tool+pattern, no new input pushed)
        var second = await gate.RequestAsync(request, CancellationToken.None);

        // Assert — second call served from session cache, no prompt required
        Assert.Equal(PermissionDecision.AllowForSession, second.Decision);
    }

    /// <summary>
    /// When user selects "Persist for project" ([p]), the permissions.json file
    /// is created with the correct schema.
    /// </summary>
    [Fact]
    public async Task Persist_WritesPermissionsJsonWithCorrectSchema()
    {
        // Arrange — full-tier model so persistent allowances are enabled
        var config = FullTierConfig();
        var store = CreateStore();

        // Navigate to "[p] Persist for project" (index 2 in full-tier list)
        var console = new TestConsole().Interactive();
        console.Input.PushKey(ConsoleKey.DownArrow);  // move to [s]
        console.Input.PushKey(ConsoleKey.DownArrow);  // move to [p]
        console.Input.PushKey(ConsoleKey.Enter);       // confirm

        var gate = new CliPermissionGate(config, store, console);
        var request = MakeRequest();

        // Act
        var response = await gate.RequestAsync(request, CancellationToken.None);

        // Assert — decision
        Assert.Equal(PermissionDecision.AllowPersisted, response.Decision);

        // Assert — file written
        Assert.True(File.Exists(PermissionsFilePath), "permissions.json must be created");
        var json = await File.ReadAllTextAsync(PermissionsFilePath);
        Assert.Contains("\"version\"", json);
        Assert.Contains("\"tools\"", json);
        Assert.Contains("write_file", json);
        Assert.Contains("allow", json);
    }

    /// <summary>
    /// When user selects "[d] Deny", the gate returns Deny with no feedback.
    /// </summary>
    [Fact]
    public async Task Deny_SelectDenyOption_ReturnsDenyWithNullFeedback()
    {
        // Arrange
        var config = FullTierConfig();
        var store = CreateStore();

        // Navigate to "[d] Deny" (index 3 in full-tier list)
        var console = new TestConsole().Interactive();
        console.Input.PushKey(ConsoleKey.DownArrow);  // [s]
        console.Input.PushKey(ConsoleKey.DownArrow);  // [p]
        console.Input.PushKey(ConsoleKey.DownArrow);  // [d]
        console.Input.PushKey(ConsoleKey.Enter);

        var gate = new CliPermissionGate(config, store, console);

        // Act
        var response = await gate.RequestAsync(MakeRequest(), CancellationToken.None);

        // Assert
        Assert.Equal(PermissionDecision.Deny, response.Decision);
        Assert.Null(response.Feedback);
    }

    /// <summary>
    /// When user selects "[r] Reject with feedback" and types a reason,
    /// the gate returns DenyWithFeedback with the entered text.
    /// </summary>
    [Fact]
    public async Task Reject_PromptsForFeedback_ReturnsDenyWithFeedback()
    {
        // Arrange
        var config = FullTierConfig();
        var store = CreateStore();

        // Navigate to "[r] Reject with feedback" (index 4 in full-tier list)
        var console = new TestConsole().Interactive();
        console.Input.PushKey(ConsoleKey.DownArrow);  // [s]
        console.Input.PushKey(ConsoleKey.DownArrow);  // [p]
        console.Input.PushKey(ConsoleKey.DownArrow);  // [d]
        console.Input.PushKey(ConsoleKey.DownArrow);  // [r]
        console.Input.PushKey(ConsoleKey.Enter);
        // Stage 2: type feedback then Enter
        console.Input.PushTextWithEnter("I don't trust this");

        var gate = new CliPermissionGate(config, store, console);

        // Act
        var response = await gate.RequestAsync(MakeRequest(), CancellationToken.None);

        // Assert
        Assert.Equal(PermissionDecision.DenyWithFeedback, response.Decision);
        Assert.Equal("I don't trust this", response.Feedback);
    }

    /// <summary>
    /// When user selects "[r] Reject with feedback" and submits an empty input,
    /// PromptFeedback defaults to "user denied" (AllowEmpty behavior).
    /// </summary>
    [Fact]
    public async Task Reject_EmptyFeedback_DefaultsToUserDenied()
    {
        // Arrange
        var config = FullTierConfig();
        var store = CreateStore();

        // Navigate to "[r] Reject with feedback" (index 4 in full-tier list)
        var console = new TestConsole().Interactive();
        console.Input.PushKey(ConsoleKey.DownArrow);  // [s]
        console.Input.PushKey(ConsoleKey.DownArrow);  // [p]
        console.Input.PushKey(ConsoleKey.DownArrow);  // [d]
        console.Input.PushKey(ConsoleKey.DownArrow);  // [r]
        console.Input.PushKey(ConsoleKey.Enter);
        // Stage 2: submit empty input (no text — just Enter)
        console.Input.PushKey(ConsoleKey.Enter);

        var gate = new CliPermissionGate(config, store, console);

        // Act
        var response = await gate.RequestAsync(MakeRequest(), CancellationToken.None);

        // Assert
        Assert.Equal(PermissionDecision.DenyWithFeedback, response.Decision);
        Assert.Equal("user denied", response.Feedback);
    }
}
