using System.Net.Http.Json;
using System.Text.Json;
using MarimerLLC.AgentRegistry.Api.Agents.Models;
using MarimerLLC.AgentRegistry.Api.Tests.Infrastructure;
using MarimerLLC.AgentRegistry.Domain.Agents;
using ModelContextProtocol.Client;

namespace MarimerLLC.AgentRegistry.Api.Tests.Protocols.MCP;

/// <summary>
/// Integration tests for the registry's own MCP server at POST /mcp (stateless Streamable HTTP).
/// Covers 2026-07-28 clients (no initialize, per-request _meta) and 2025-11-25 clients (initialize
/// handshake, served without a session). The SDK converts PascalCase method names to snake_case.
/// Responses arrive as SSE (text/event-stream) or JSON with JSON-RPC payloads.
/// </summary>
public class McpServerTests(AgentRegistryFactory factory) : IClassFixture<AgentRegistryFactory>, IDisposable
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly HttpClient _admin = factory.CreateAdminClient();

    public void Dispose() => factory.Reset();

    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    private const string Modern = "2026-07-28";
    private const string Legacy = "2025-11-25";

    // Tool name constants — SDK converts PascalCase method names to snake_case.
    private const string ToolDiscoverAgents = "discover_agents";
    private const string ToolGetAgent = "get_agent";
    private const string ToolGetA2ACard = "get_a2_a_card";   // GetA2ACard → get_a2_a_card
    private const string ToolGetMcpServerCard = "get_mcp_server_card";
    private const string ToolGetAcpManifest = "get_acp_manifest";

    // ── 2025-11-25 down-level: initialize handshake ───────────────────────────

    [Fact]
    public async Task Initialize_ReturnsServerInfo()
    {
        var (result, _) = await Initialize();

        var serverInfo = result.GetProperty("serverInfo");
        Assert.Equal(RegistryIdentity.Name, serverInfo.GetProperty("name").GetString());
        Assert.Equal(RegistryIdentity.Title, serverInfo.GetProperty("title").GetString());
        Assert.Equal(RegistryIdentity.Version, serverInfo.GetProperty("version").GetString());
        Assert.Equal(Legacy, result.GetProperty("protocolVersion").GetString());
    }

    [Fact]
    public async Task Initialize_ReturnsToolsCapabilityAndInstructions()
    {
        var (result, _) = await Initialize();

        Assert.True(result.GetProperty("capabilities").TryGetProperty("tools", out _),
            "Server capabilities should include 'tools'");
        Assert.Contains("discover_agents", result.GetProperty("instructions").GetString());
    }

    [Fact]
    public async Task Initialize_IsStateless_NoSessionId()
    {
        var (_, sessionId) = await Initialize();
        Assert.Null(sessionId);
    }

    [Fact]
    public async Task McpEndpoint_IsPublic_NoAuthRequired()
    {
        var (result, _) = await Initialize();
        Assert.Equal(RegistryIdentity.Name, result.GetProperty("serverInfo").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Legacy_ToolsList_ReturnsAllFiveTools()
    {
        await Initialize();
        var result = await CallLegacy("tools/list", new { });

        var names = result.GetProperty("tools").EnumerateArray()
            .Select(t => t.GetProperty("name").GetString()).ToHashSet();
        Assert.Equal(5, names.Count);
        Assert.Contains(ToolDiscoverAgents, names);
        Assert.Contains(ToolGetAgent, names);
        Assert.Contains(ToolGetA2ACard, names);
        Assert.Contains(ToolGetMcpServerCard, names);
        Assert.Contains(ToolGetAcpManifest, names);
    }

    [Fact]
    public async Task Legacy_ToolCall_DiscoverAgents_ReturnsAgentList()
    {
        await RegisterA2AAgent();

        await Initialize();
        var result = await CallLegacy("tools/call",
            new { name = ToolDiscoverAgents, arguments = new { liveOnly = true, pageSize = 10 } });

        var payload = ParseToolText(result);
        Assert.Equal(1, payload.GetProperty("totalCount").GetInt32());
    }

    // ── 2026-07-28: no initialize, per-request _meta ──────────────────────────

    [Fact]
    public async Task Discover_ListsModernAndServerInfo()
    {
        var result = await CallModern("server/discover", new { });

        var versions = result.GetProperty("supportedVersions").EnumerateArray()
            .Select(v => v.GetString()).ToList();
        Assert.Contains(Modern, versions);
        Assert.True(result.GetProperty("capabilities").TryGetProperty("tools", out _));
        Assert.Contains("discover_agents", result.GetProperty("instructions").GetString());

        var serverInfo = result.GetProperty("_meta").GetProperty("io.modelcontextprotocol/serverInfo");
        Assert.Equal(RegistryIdentity.Name, serverInfo.GetProperty("name").GetString());
    }

    [Fact]
    public async Task ToolsList_ReturnsAllFiveTools()
    {
        var result = await CallModern("tools/list", new { });

        var tools = result.GetProperty("tools").EnumerateArray().ToList();
        Assert.Equal(5, tools.Count);

        var names = tools.Select(t => t.GetProperty("name").GetString()).ToHashSet();
        Assert.Contains(ToolDiscoverAgents, names);
        Assert.Contains(ToolGetAgent, names);
        Assert.Contains(ToolGetA2ACard, names);
        Assert.Contains(ToolGetMcpServerCard, names);
        Assert.Contains(ToolGetAcpManifest, names);
    }

    [Fact]
    public async Task ToolsList_AllToolsHaveDescriptions()
    {
        var result = await CallModern("tools/list", new { });

        Assert.All(result.GetProperty("tools").EnumerateArray(), tool =>
        {
            Assert.True(tool.TryGetProperty("description", out var desc));
            Assert.False(string.IsNullOrWhiteSpace(desc.GetString()));
        });
    }

    [Fact]
    public async Task ToolsList_AllToolsAnnotatedReadOnly()
    {
        var result = await CallModern("tools/list", new { });

        Assert.All(result.GetProperty("tools").EnumerateArray(), tool =>
        {
            var annotations = tool.GetProperty("annotations");
            Assert.True(annotations.GetProperty("readOnlyHint").GetBoolean());
            Assert.True(annotations.GetProperty("idempotentHint").GetBoolean());
            Assert.False(annotations.GetProperty("openWorldHint").GetBoolean());
        });
    }

    [Fact]
    public async Task ToolsList_DiscoverAgents_HasInputSchema()
    {
        var result = await CallModern("tools/list", new { });

        var discover = result.GetProperty("tools").EnumerateArray()
            .First(t => t.GetProperty("name").GetString() == ToolDiscoverAgents);

        Assert.True(discover.TryGetProperty("inputSchema", out _));
    }

    [Fact]
    public async Task ToolCall_DiscoverAgents_ReturnsAgentList()
    {
        await RegisterA2AAgent();

        var result = await CallToolModern(ToolDiscoverAgents, new { liveOnly = true, pageSize = 10 });

        Assert.True(result.TryGetProperty("agents", out _));
        Assert.Equal(1, result.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task ToolCall_DiscoverAgents_EmptyRegistry_ReturnsEmptyList()
    {
        var result = await CallToolModern(ToolDiscoverAgents, new { liveOnly = true });
        Assert.Equal(0, result.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task ToolCall_GetAgent_UnknownId_ReturnsErrorObject()
    {
        var result = await CallToolModern(ToolGetAgent, new { id = Guid.NewGuid().ToString() });
        Assert.True(result.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task ToolCall_GetAgent_InvalidId_ReturnsErrorObject()
    {
        var result = await CallToolModern(ToolGetAgent, new { id = "not-a-guid" });
        Assert.True(result.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task ToolCall_GetAgent_KnownAgent_ReturnsDetails()
    {
        var created = await PostAndDeserialize<AgentResponse>("/agents",
            new RegisterAgentRequest("Known Agent", "desc", null, null, null), _admin);

        var result = await CallToolModern(ToolGetAgent, new { id = created.Id });

        Assert.Equal("Known Agent", result.GetProperty("name").GetString());
        Assert.Equal(created.Id, result.GetProperty("id").GetString());
    }

    [Fact]
    public async Task ToolCall_GetA2ACard_NoA2AEndpoints_ReturnsError()
    {
        var created = await RegisterMcpAgent();

        var result = await CallToolModern(ToolGetA2ACard, new { id = created.Id });

        Assert.True(result.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task ToolCall_GetMcpServerCard_ReturnsServerCard()
    {
        var created = await RegisterMcpAgent();

        var result = await CallToolModern(ToolGetMcpServerCard, new { id = created.Id });

        Assert.Equal(
            "https://static.modelcontextprotocol.io/schemas/v1/server-card.schema.json",
            result.GetProperty("$schema").GetString());
        Assert.Equal("https://example.com/mcp",
            result.GetProperty("remotes")[0].GetProperty("url").GetString());
    }

    [Fact]
    public async Task Modern_ToolCallWithoutMcpNameHeader_IsRejected()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 7,
                method = "tools/call",
                @params = new Dictionary<string, object>
                {
                    ["name"] = ToolDiscoverAgents,
                    ["arguments"] = new { },
                    ["_meta"] = new Dictionary<string, object>
                    {
                        ["io.modelcontextprotocol/protocolVersion"] = Modern,
                        ["io.modelcontextprotocol/clientCapabilities"] = new { },
                    },
                },
            }, options: Json)
        };
        request.Headers.Add("Accept", "application/json, text/event-stream");
        request.Headers.Add("MCP-Protocol-Version", Modern);
        request.Headers.Add("Mcp-Method", "tools/call");

        var response = await _client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Real SDK client ───────────────────────────────────────────────────────

    [Fact]
    public async Task SdkClient_Modern_DiscoversAgents()
    {
        await RegisterA2AAgent();

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(_client.BaseAddress!, "/mcp") },
            _client,
            ownsHttpClient: false);

        await using var mcp = await McpClient.CreateAsync(transport,
            new McpClientOptions { ProtocolVersion = Modern });

        Assert.Equal(Modern, mcp.NegotiatedProtocolVersion);

        var tools = await mcp.ListToolsAsync();
        Assert.Contains(tools, t => t.Name == ToolDiscoverAgents);

        var result = await mcp.CallToolAsync(ToolDiscoverAgents,
            new Dictionary<string, object?> { ["liveOnly"] = true });
        var text = result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Single().Text;
        Assert.Equal(1, JsonDocument.Parse(text).RootElement.GetProperty("totalCount").GetInt32());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task RegisterA2AAgent() =>
        (await _admin.PostAsJsonAsync("/agents", new RegisterAgentRequest(
            "Discovery Test", null, null,
            [new CapabilityRequest("search", "Searches", ["search"])],
            [new EndpointRequest("ep", TransportType.Http, ProtocolType.A2A,
                "https://example.com", LivenessModel.Ephemeral, 300, null, null)]))).EnsureSuccessStatusCode();

    private Task<AgentResponse> RegisterMcpAgent() =>
        PostAndDeserialize<AgentResponse>("/agents",
            new RegisterAgentRequest("MCP Agent", null, null, null,
            [new EndpointRequest("ep", TransportType.Http, ProtocolType.MCP,
                "https://example.com/mcp", LivenessModel.Ephemeral, 300, null, null)]), _admin);

    /// <summary>2025-11-25 initialize. Returns the result and any Mcp-Session-Id (none when stateless).</summary>
    private async Task<(JsonElement result, string? sessionId)> Initialize()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = Legacy,
                    capabilities = new { },
                    clientInfo = new { name = "test-client", version = "1.0" }
                }
            }, options: Json)
        };
        request.Headers.Add("Accept", "application/json, text/event-stream");

        var response = await _client.SendAsync(request);
        Assert.True(response.IsSuccessStatusCode, $"Initialize failed: {response.StatusCode}");

        var sessionId = response.Headers.TryGetValues("Mcp-Session-Id", out var vals)
            ? vals.FirstOrDefault()
            : null;

        var root = JsonDocument.Parse(ExtractJsonFromSse(await response.Content.ReadAsStringAsync())).RootElement;
        return (root.TryGetProperty("result", out var r) ? r : root, sessionId);
    }

    /// <summary>A 2025-11-25 request after initialize: protocol version in the header, no _meta.</summary>
    private Task<JsonElement> CallLegacy(string method, object @params)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = Random.Shared.Next(100, 9999),
                method,
                @params
            }, options: Json)
        };
        request.Headers.Add("MCP-Protocol-Version", Legacy);
        return Send(request, method);
    }

    /// <summary>
    /// A 2026-07-28 request: no initialize; protocol version and client capabilities in
    /// params._meta, plus the required Mcp-Method / Mcp-Name headers.
    /// </summary>
    private Task<JsonElement> CallModern(string method, Dictionary<string, object?> @params, string? routingName = null)
    {
        @params["_meta"] = new Dictionary<string, object>
        {
            ["io.modelcontextprotocol/protocolVersion"] = Modern,
            ["io.modelcontextprotocol/clientCapabilities"] = new { },
            ["io.modelcontextprotocol/clientInfo"] = new { name = "test-client", version = "1.0" },
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = Random.Shared.Next(100, 9999),
                method,
                @params
            }, options: Json)
        };
        request.Headers.Add("MCP-Protocol-Version", Modern);
        request.Headers.Add("Mcp-Method", method);
        if (routingName is not null) request.Headers.Add("Mcp-Name", routingName);
        return Send(request, method);
    }

    private Task<JsonElement> CallModern(string method, object @params) =>
        CallModern(method, JsonSerializer.Deserialize<Dictionary<string, object?>>(
            JsonSerializer.Serialize(@params, Json), Json)!);

    private async Task<JsonElement> CallToolModern(string toolName, object arguments)
    {
        var result = await CallModern("tools/call",
            new Dictionary<string, object?> { ["name"] = toolName, ["arguments"] = arguments },
            routingName: toolName);
        return ParseToolText(result);
    }

    private async Task<JsonElement> Send(HttpRequestMessage request, string method)
    {
        request.Headers.Add("Accept", "application/json, text/event-stream");

        var response = await _client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode,
            $"MCP {method} returned {response.StatusCode}. Body: {text[..Math.Min(500, text.Length)]}");
        Assert.False(response.Headers.Contains("Mcp-Session-Id"), "Stateless server must not mint a session");

        var root = JsonDocument.Parse(ExtractJsonFromSse(text)).RootElement;
        Assert.False(root.TryGetProperty("error", out var error), $"MCP {method} error: {error}");
        return root.GetProperty("result");
    }

    /// <summary>Tool results carry the JSON payload in content[0].text.</summary>
    private static JsonElement ParseToolText(JsonElement result) =>
        JsonDocument.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!).RootElement;

    /// <summary>
    /// Extracts the JSON-RPC payload from an SSE stream or plain JSON response.
    /// Prefers the data: line that contains a JSON-RPC result or error.
    /// </summary>
    private static string ExtractJsonFromSse(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            return text;

        var candidates = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("data:"))
            .Select(l => l["data:".Length..].Trim())
            .Where(l => l.StartsWith('{'))
            .ToList();

        return candidates.FirstOrDefault(c => c.Contains("\"result\"") || c.Contains("\"error\""))
            ?? candidates.FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"No JSON payload found in SSE: {text[..Math.Min(300, text.Length)]}");
    }

    private static async Task<T> PostAndDeserialize<T>(string url, object body, HttpClient client)
    {
        var response = await client.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
