using Nexus.Core.Config;
using Xunit;

namespace Nexus.Core.Tests;

public class McpPersistenceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;

    public McpPersistenceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"nexus-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, "nexus.yaml");
    }

    [Fact]
    public void Save_WithMcpServer_ThenLoad_RoundTrips()
    {
        // Arrange
        var config = new NexusConfig();
        config.Mcp.Servers.Add(new McpServerEntry
        {
            Name = "filesystem",
            Transport = "stdio",
            Command = "npx",
            Args = ["-y", "@modelcontextprotocol/server-filesystem", "/tmp"]
        });

        // Act
        ConfigLoader.Save(config, _configPath);
        var loaded = ConfigLoader.Load(_configPath);

        // Assert
        Assert.Single(loaded.Mcp.Servers);
        var server = loaded.Mcp.Servers[0];
        Assert.Equal("filesystem", server.Name);
        Assert.Equal("stdio", server.Transport);
        Assert.Equal("npx", server.Command);
        Assert.Equal(["-y", "@modelcontextprotocol/server-filesystem", "/tmp"], server.Args);
    }

    [Fact]
    public void Save_DuplicateServerName_ReplacesExisting()
    {
        // Arrange
        var config = new NexusConfig();
        config.Mcp.Servers.Add(new McpServerEntry
        {
            Name = "myserver",
            Transport = "stdio",
            Command = "old-command"
        });
        ConfigLoader.Save(config, _configPath);

        // Act — replace with new entry of same name
        config.Mcp.Servers.RemoveAll(s => string.Equals(s.Name, "myserver", StringComparison.OrdinalIgnoreCase));
        config.Mcp.Servers.Add(new McpServerEntry
        {
            Name = "myserver",
            Transport = "stdio",
            Command = "new-command",
            Args = ["--flag"]
        });
        ConfigLoader.Save(config, _configPath);
        var loaded = ConfigLoader.Load(_configPath);

        // Assert
        Assert.Single(loaded.Mcp.Servers);
        Assert.Equal("new-command", loaded.Mcp.Servers[0].Command);
        Assert.Equal(["--flag"], loaded.Mcp.Servers[0].Args);
    }

    [Fact]
    public void Save_AfterRemovingServer_PersistsRemoval()
    {
        // Arrange
        var config = new NexusConfig();
        config.Mcp.Servers.Add(new McpServerEntry
        {
            Name = "to-remove",
            Transport = "stdio",
            Command = "some-cmd"
        });
        ConfigLoader.Save(config, _configPath);

        // Act — remove and save
        config.Mcp.Servers.RemoveAll(s => string.Equals(s.Name, "to-remove", StringComparison.OrdinalIgnoreCase));
        ConfigLoader.Save(config, _configPath);
        var loaded = ConfigLoader.Load(_configPath);

        // Assert
        Assert.Empty(loaded.Mcp.Servers);
    }

    [Fact]
    public void Save_WithSseServer_RoundTripsUrl()
    {
        // Arrange
        var config = new NexusConfig();
        config.Mcp.Servers.Add(new McpServerEntry
        {
            Name = "remote-tools",
            Transport = "sse",
            Url = "http://localhost:3001/sse"
        });

        // Act
        ConfigLoader.Save(config, _configPath);
        var loaded = ConfigLoader.Load(_configPath);

        // Assert
        Assert.Single(loaded.Mcp.Servers);
        var server = loaded.Mcp.Servers[0];
        Assert.Equal("remote-tools", server.Name);
        Assert.Equal("sse", server.Transport);
        Assert.Equal("http://localhost:3001/sse", server.Url);
    }

    [Fact]
    public void Save_WithEnvDictionary_RoundTrips()
    {
        // Arrange
        var config = new NexusConfig();
        config.Mcp.Servers.Add(new McpServerEntry
        {
            Name = "env-server",
            Transport = "stdio",
            Command = "node",
            Args = ["server.js"],
            Env = new Dictionary<string, string>
            {
                ["NODE_ENV"] = "production",
                ["API_KEY"] = "test-key-123"
            }
        });

        // Act
        ConfigLoader.Save(config, _configPath);
        var loaded = ConfigLoader.Load(_configPath);

        // Assert
        Assert.Single(loaded.Mcp.Servers);
        var server = loaded.Mcp.Servers[0];
        Assert.Equal(2, server.Env.Count);
        Assert.Equal("production", server.Env["NODE_ENV"]);
        Assert.Equal("test-key-123", server.Env["API_KEY"]);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup
        }
    }
}
