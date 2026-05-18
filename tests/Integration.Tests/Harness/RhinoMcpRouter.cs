using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace RhMcp.Integration.Tests.Harness;

// Spawns the rhino-mcp-router binary as a child process and connects an MCP
// stdio client to it. Each instance gets its own TMPDIR so the router's
// state.db / listener-announcement dir are isolated from the user's regularly
// running router. Disposal kills the child process via the transport's
// shutdown path.
public sealed class RhinoMcpRouter : IAsyncDisposable
{
    private readonly string _isolatedTempDir;

    public McpClient Client { get; }

    public HashSet<string> OpenedSlots { get; } = new();

    // Mirrors RouterPaths.ListenersDir on the router side. Tests can drop
    // *.json announcement files here to simulate a user-started Rhino.
    public string ListenersDir => Path.Combine(_isolatedTempDir, "rhino-mcp", "listeners");

    private RhinoMcpRouter(string isolatedTempDir, McpClient client)
    {
        _isolatedTempDir = isolatedTempDir;
        Client = client;
    }

    public static async Task<RhinoMcpRouter> LaunchIsolatedAsync(CancellationToken ct = default)
    {
        string isolatedTempDir = RhinoRouterPaths.CreateIsolatedTempDir();
        StdioClientTransportOptions options = new()
        {
            Name = "rhino-mcp-router (test)",
            Command = RhinoRouterPaths.ResolveBinary(),
            EnvironmentVariables = RhinoRouterPaths.IsolatedEnv(isolatedTempDir),
        };
        StdioClientTransport transport = new(options);

        try
        {
            McpClient client = await McpClient.CreateAsync(transport, cancellationToken: ct);
            return new RhinoMcpRouter(isolatedTempDir, client);
        }
        catch
        {
            RhinoRouterPaths.TryDeleteDirectory(isolatedTempDir);
            throw;
        }
    }

    public async Task<string> CallToolTextAsync(
        string toolName,
        Dictionary<string, object?>? arguments = null,
        CancellationToken ct = default)
    {
        CallToolResult result = await Client.CallToolAsync(toolName, arguments, cancellationToken: ct);
        // TODO : Stash any slots opened by this client
        // TODO : Remove any slots closed by this client
        return string.Concat(result.Content.OfType<TextContentBlock>().Select(t => t.Text));
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            // TODO : Close any slots opened by this client
            for (int i = 0; i < 5; i++)
            {
                _ = await CallToolTextAsync("_router_close_listener", new() { { "port", i } });
            }

            await Client.DisposeAsync();
        }
        catch { /* best effort */ }
        RhinoRouterPaths.TryDeleteDirectory(_isolatedTempDir);
    }
}

internal static class JsonAssert
{
    public static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;
}
