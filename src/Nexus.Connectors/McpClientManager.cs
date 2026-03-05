using Microsoft.Extensions.Logging;

namespace Nexus.Connectors;

public class McpServerConfig
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool IsConnected { get; set; }
}

public class McpClientManager
{
    private readonly List<McpServerConfig> _servers = new();
    private readonly ILogger<McpClientManager>? _logger;

    public McpClientManager(ILogger<McpClientManager>? logger = null)
    {
        _logger = logger;
    }

    public IReadOnlyList<McpServerConfig> Servers => _servers.AsReadOnly();

    public async Task<bool> ConnectAsync(string name, string url, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Connecting to MCP server {Name} at {Url}", name, url);
        
        var server = new McpServerConfig { Name = name, Url = url };
        
        try
        {
            // Verify the server is reachable with a simple HTTP ping
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.GetAsync(url, cancellationToken);
            server.IsConnected = response.IsSuccessStatusCode || (int)response.StatusCode < 500;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Could not connect to MCP server {Name}", name);
            server.IsConnected = false;
        }

        var existing = _servers.FirstOrDefault(s => s.Url == url);
        if (existing != null)
            _servers.Remove(existing);
        _servers.Add(server);

        return server.IsConnected;
    }

    public void Disconnect(string name)
    {
        var server = _servers.FirstOrDefault(s => s.Name == name);
        if (server != null)
        {
            server.IsConnected = false;
            _logger?.LogInformation("Disconnected from MCP server {Name}", name);
        }
    }

    public McpServerConfig? GetServer(string name) =>
        _servers.FirstOrDefault(s => s.Name == name);
}
