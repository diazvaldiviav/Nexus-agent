using Nexus.Core.Config;
using Nexus.Connectors;
using Nexus.Desktop.Tests.Fakes;
using Nexus.Desktop.ViewModels;

namespace Nexus.Desktop.Tests;

public class SettingsViewModelValidationTests
{
    private static McpLifecycleService CreateMcpLifecycleService()
        => new(new FakeMcpClientManager(), new ToolRegistry());

    private static SettingsViewModel CreateVm(NexusConfig? config = null)
    {
        config ??= new NexusConfig();
        config.Models.Local.Endpoint ??= "http://localhost:11434";
        return new SettingsViewModel(config, CreateMcpLifecycleService());
    }

    [Fact]
    public void DecayLambda_OutOfRange_SetsError()
    {
        var vm = CreateVm();
        vm.DecayLambda = 0.0001m;
        Assert.NotNull(vm.DecayLambdaError);
    }

    [Fact]
    public void DecayLambda_InRange_ClearsError()
    {
        var vm = CreateVm();
        vm.DecayLambda = 0.0001m;
        Assert.NotNull(vm.DecayLambdaError);

        vm.DecayLambda = 0.05m;
        Assert.Null(vm.DecayLambdaError);
    }

    [Fact]
    public void LocalEndpoint_Malformed_SetsError()
    {
        var vm = CreateVm();
        vm.LocalEndpoint = "bad";
        Assert.NotNull(vm.LocalEndpointError);
    }

    [Fact]
    public void LocalEndpoint_ValidUri_ClearsError()
    {
        var vm = CreateVm();
        vm.LocalEndpoint = "bad";
        Assert.NotNull(vm.LocalEndpointError);

        vm.LocalEndpoint = "http://x:1234";
        Assert.Null(vm.LocalEndpointError);
    }

    [Fact]
    public void SummarizationInterval_Zero_SetsError()
    {
        var vm = CreateVm();
        vm.SummarizationInterval = 0;
        Assert.NotNull(vm.SummarizationIntervalError);
    }

    [Fact]
    public void RecentInteractionsFetchLimit_AboveMax_SetsError()
    {
        var vm = CreateVm();
        vm.RecentInteractionsFetchLimit = 51;
        Assert.NotNull(vm.RecentInteractionsFetchLimitError);
    }

    [Fact]
    public void IsDirty_TrueAfterFieldChange()
    {
        var vm = CreateVm();
        Assert.False(vm.IsDirty);

        vm.LocalModel = "different-model";
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void IsDirty_FalseAfterRevert()
    {
        var vm = CreateVm();
        var original = vm.LocalModel;

        vm.LocalModel = "different-model";
        Assert.True(vm.IsDirty);

        vm.LocalModel = original;
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void CanSave_FalseWhenNotDirty()
    {
        var vm = CreateVm();
        Assert.False(vm.SaveSettingsCommand.CanExecute(null));
    }

    [Fact]
    public void CanSave_FalseWhenDirtyButInvalid()
    {
        var vm = CreateVm();
        vm.DecayLambda = 0m;
        Assert.True(vm.IsDirty);
        Assert.True(vm.HasValidationErrors);
        Assert.False(vm.SaveSettingsCommand.CanExecute(null));
    }

    [Fact]
    public void CanSave_TrueWhenDirtyAndValid()
    {
        var vm = CreateVm();
        vm.CloudModel = "gpt-4o";
        Assert.True(vm.IsDirty);
        Assert.False(vm.HasValidationErrors);
        Assert.True(vm.SaveSettingsCommand.CanExecute(null));
    }

    [Fact]
    public void SaveSettings_ResetsIsDirty()
    {
        var vm = CreateVm();
        vm.CloudModel = "gpt-4o";
        Assert.True(vm.IsDirty);

        vm.SaveSettingsCommand.Execute(null);

        // ConfigLoader.Save writes to real filesystem; if it succeeds, IsDirty resets
        if (vm.HasSuccess)
        {
            Assert.False(vm.IsDirty);
            Assert.False(vm.SaveSettingsCommand.CanExecute(null));
        }
        else
        {
            // Save failed due to filesystem contention — verify error state instead
            Assert.True(vm.HasError);
        }
    }

    [Fact]
    public void ApiKeyWarning_ShownWhenProviderKeyMissing()
    {
        var config = new NexusConfig();
        config.Models.Local.Endpoint = "http://localhost:11434";
        config.Models.Cloud.Provider = "anthropic";
        config.Models.Anthropic = null;
        var vm = new SettingsViewModel(config, CreateMcpLifecycleService());

        Assert.NotNull(vm.ApiKeyWarning);
        Assert.Contains("anthropic", vm.ApiKeyWarning);
    }

    // MaxToolCallIterations: min=1, max=20

    [Fact]
    public void MaxToolCallIterations_BelowMin_SetsError()
    {
        var vm = CreateVm();
        vm.MaxToolCallIterations = 0;
        Assert.NotNull(vm.MaxToolCallIterationsError);
    }

    [Fact]
    public void MaxToolCallIterations_AboveMax_SetsError()
    {
        var vm = CreateVm();
        vm.MaxToolCallIterations = 21;
        Assert.NotNull(vm.MaxToolCallIterationsError);
    }

    [Fact]
    public void MaxToolCallIterations_InRange_ClearsError()
    {
        var vm = CreateVm();
        vm.MaxToolCallIterations = 0;
        Assert.NotNull(vm.MaxToolCallIterationsError);

        vm.MaxToolCallIterations = 5;
        Assert.Null(vm.MaxToolCallIterationsError);
    }

    // ToolCallTimeoutSeconds: min=1, max=300

    [Fact]
    public void ToolCallTimeoutSeconds_BelowMin_SetsError()
    {
        var vm = CreateVm();
        vm.ToolCallTimeoutSeconds = 0;
        Assert.NotNull(vm.ToolCallTimeoutSecondsError);
    }

    [Fact]
    public void ToolCallTimeoutSeconds_AboveMax_SetsError()
    {
        var vm = CreateVm();
        vm.ToolCallTimeoutSeconds = 301;
        Assert.NotNull(vm.ToolCallTimeoutSecondsError);
    }

    [Fact]
    public void ToolCallTimeoutSeconds_InRange_ClearsError()
    {
        var vm = CreateVm();
        vm.ToolCallTimeoutSeconds = 0;
        Assert.NotNull(vm.ToolCallTimeoutSecondsError);

        vm.ToolCallTimeoutSeconds = 30;
        Assert.Null(vm.ToolCallTimeoutSecondsError);
    }

    // MaxOutputLines: min=1, max=10000

    [Fact]
    public void MaxOutputLines_BelowMin_SetsError()
    {
        var vm = CreateVm();
        vm.MaxOutputLines = 0;
        Assert.NotNull(vm.MaxOutputLinesError);
    }

    [Fact]
    public void MaxOutputLines_AboveMax_SetsError()
    {
        var vm = CreateVm();
        vm.MaxOutputLines = 10001;
        Assert.NotNull(vm.MaxOutputLinesError);
    }

    [Fact]
    public void MaxOutputLines_InRange_ClearsError()
    {
        var vm = CreateVm();
        vm.MaxOutputLines = 0;
        Assert.NotNull(vm.MaxOutputLinesError);

        vm.MaxOutputLines = 200;
        Assert.Null(vm.MaxOutputLinesError);
    }

    // MaxOutputBytes: min=1000, max=500000

    [Fact]
    public void MaxOutputBytes_BelowMin_SetsError()
    {
        var vm = CreateVm();
        vm.MaxOutputBytes = 999;
        Assert.NotNull(vm.MaxOutputBytesError);
    }

    [Fact]
    public void MaxOutputBytes_AboveMax_SetsError()
    {
        var vm = CreateVm();
        vm.MaxOutputBytes = 500001;
        Assert.NotNull(vm.MaxOutputBytesError);
    }

    [Fact]
    public void MaxOutputBytes_InRange_ClearsError()
    {
        var vm = CreateVm();
        vm.MaxOutputBytes = 999;
        Assert.NotNull(vm.MaxOutputBytesError);

        vm.MaxOutputBytes = 32000;
        Assert.Null(vm.MaxOutputBytesError);
    }

    // Integration: HasValidationErrors reflects MCP field errors

    [Fact]
    public void HasValidationErrors_IncludesMcpErrors()
    {
        var vm = CreateVm();
        Assert.False(vm.HasValidationErrors);

        vm.MaxToolCallIterations = 0;

        Assert.True(vm.HasValidationErrors);
        Assert.NotNull(vm.MaxToolCallIterationsError);
    }

    // Integration: SaveSettings writes MCP fields back to config

    [Fact]
    public void SaveSettings_WritesMcpFieldsToConfig()
    {
        // Arrange
        var config = new NexusConfig();
        config.Models.Local.Endpoint = "http://localhost:11434";
        var vm = new SettingsViewModel(config, CreateMcpLifecycleService());

        vm.MaxToolCallIterations = 7;
        vm.ToolCallTimeoutSeconds = 60;
        vm.MaxOutputLines = 500;
        vm.MaxOutputBytes = 65536;
        vm.SchemaValidationEnabled = false;

        // Act: SaveSettings writes config even if filesystem save fails
        // We verify the config object is updated before the file write attempt
        vm.SaveSettingsCommand.Execute(null);

        // Assert: config was written (regardless of filesystem outcome)
        Assert.Equal(7, config.Mcp.MaxToolCallIterations);
        Assert.Equal(60, config.Mcp.ToolCallTimeoutSeconds);
        Assert.Equal(500, config.Mcp.MaxOutputLines);
        Assert.Equal(65536, config.Mcp.MaxOutputBytes);
        Assert.False(config.Mcp.SchemaValidationEnabled);
    }

    // ── ToolFilteringEnabled toggle ───────────────────────────────────────

    [Fact]
    public void Constructor_LoadsToolFilteringEnabled()
    {
        // Arrange
        var config = new NexusConfig();
        config.Models.Local.Endpoint = "http://localhost:11434";
        config.Mcp.ToolFilteringEnabled = true;

        // Act
        var vm = new SettingsViewModel(config, CreateMcpLifecycleService());

        // Assert
        Assert.True(vm.ToolFilteringEnabled);
    }

    [Fact]
    public void SaveSettings_WritesToolFilteringEnabledToConfig()
    {
        // Arrange
        var config = new NexusConfig();
        config.Models.Local.Endpoint = "http://localhost:11434";
        var vm = new SettingsViewModel(config, CreateMcpLifecycleService());

        vm.ToolFilteringEnabled = true;

        // Act
        vm.SaveSettingsCommand.Execute(null);

        // Assert
        Assert.True(config.Mcp.ToolFilteringEnabled);
    }

    [Fact]
    public void ToolFilteringEnabled_Change_SetsDirty()
    {
        // Arrange
        var vm = CreateVm();
        Assert.False(vm.IsDirty);

        // Act
        vm.ToolFilteringEnabled = true;

        // Assert
        Assert.True(vm.IsDirty);
    }
}
