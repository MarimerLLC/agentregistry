using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentRegistry.Api.Agents.Models;
using AgentRegistry.Api.Tests.Infrastructure;
using AgentRegistry.Domain.Agents;

namespace AgentRegistry.Api.Tests.Protocols.MCP;

/// <summary>
/// Integration tests for the registry's own MCP server at POST /mcp (Streamable HTTP).
/// The SDK converts PascalCase method names to snake_case for tool names.
/// Responses arrive as SSE (text/event-stream) with JSON-RPC payloads in data: lines.
/// </summary>
public class McpServerTests(AgentRegistryFactory factory) : IClassFixture<AgentRegistryFactory>, IDisposable
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly HttpClient _admin = factory.CreateAdminClient();

    public void Dispose() => factory.Reset();

    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    // Tool name constants — SDK converts PascalCase method names to snake_case.
    private const string ToolDiscoverAgents = "discover_agents";
    private const string ToolGetAgent = "get_agent";
    private const string ToolGetA2ACard = "get_a2_a_card";   // GetA2ACard → get_a2_a_card
    private const string ToolGetMcpServerCard = "get_mcp_server_card";
    private const string ToolGetAcpManifest = "get_acp_manifest";

    // ── MCP protocol handshake ────────────────────────────────────────────────

    [Fact]
    public async Task Initialize_ReturnsServerInfo()
    {
        var (result, _) = await Initialize();

        Assert.Equal("AgentRegistry",
            result.GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.Equal("1.0.0",
            result.GetProperty("serverInfo").GetProperty("version").GetString());
    }

    [Fact]
    public async Task Initialize_ReturnsToolsCapability()
    {
        var (result, _) = await Initialize();

        var capabilities = result.GetProperty("capabilities");
        Assert.True(capabilities.TryGetProperty("tools", out _),
            "Server capabilities should include 'tools'");
    }

    [Fact]
    public async Task McpEndpoint_IsPublic_NoAuthRequired()
    {
        var (result, _) = await Initialize();
        Assert.Equal("AgentRegistry", result.GetProperty("serverInfo").GetProperty("name").GetString());
    }

    // ── Tool discovery ────────────────────────────────────────────────────────

    [Fact]
    public async Task ToolsList_ReturnsAllFiveTools()
    {
        var (_, sessionId) = await Initialize();
        var result = await CallMethod("tools/list", new { }, sessionId);

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
        var (_, sessionId) = await Initialize();
        var result = await CallMethod("tools/list", new { }, sessionId);

        var tools = result.GetProperty("tools").EnumerateArray().ToList();
        Assert.All(tools, tool =>
        {
            Assert.True(tool.TryGetProperty("description", out var desc));
            Assert.False(string.IsNullOrWhiteSpace(desc.GetString()));
        });
    }

    [Fact]
    public async Task ToolsList_DiscoverAgents_HasInputSchema()
    {
        var (_, sessionId) = await Initialize();
        var result = await CallMethod("tools/list", new { }, sessionId);

        var discover = result.GetProperty("tools").EnumerateArray()
            .First(t => t.GetProperty("name").GetString() == ToolDiscoverAgents);

        Assert.True(discover.TryGetProperty("inputSchema", out _));
    }

    // ── Tool calls ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ToolCall_DiscoverAgents_ReturnsAgentList()
    {
        await _admin.PostAsJsonAsync("/agents", new RegisterAgentRequest(
            "Discovery Test", null, null,
            [new CapabilityRequest("search", "Searches", ["search"])],
            [new EndpointRequest("ep", TransportType.Http, ProtocolType.A2A,
                "https://example.com", LivenessModel.Ephemeral, 300, null, null)]));

        var (_, sessionId) = await Initialize();
        var result = await CallToolAndParseText(ToolDiscoverAgents,
            new { liveOnly = true, pageSize = 10 }, sessionId);

        Assert.True(result.TryGetProperty("agents", out _));
        Assert.True(result.TryGetProperty("totalCount", out _));
    }

    [Fact]
    public async Task ToolCall_DiscoverAgents_EmptyRegistry_ReturnsEmptyList()
    {
        var (_, sessionId) = await Initialize();
        var result = await CallToolAndParseText(ToolDiscoverAgents,
            new { liveOnly = true }, sessionId);

        Assert.Equal(0, result.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task ToolCall_GetAgent_UnknownId_ReturnsErrorObject()
    {
        var (_, sessionId) = await Initialize();
        var result = await CallToolAndParseText(ToolGetAgent,
            new { id = Guid.NewGuid().ToString() }, sessionId);

        Assert.True(result.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task ToolCall_GetAgent_InvalidId_ReturnsErrorObject()
    {
        var (_, sessionId) = await Initialize();
        var result = await CallToolAndParseText(ToolGetAgent,
            new { id = "not-a-guid" }, sessionId);

        Assert.True(result.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task ToolCall_GetAgent_KnownAgent_ReturnsDetails()
    {
        var created = await PostAndDeserialize<AgentResponse>("/agents",
            new RegisterAgentRequest("Known Agent", "desc", null, null, null), _admin);

        var (_, sessionId) = await Initialize();
        var result = await CallToolAndParseText(ToolGetAgent,
            new { id = created.Id }, sessionId);

        Assert.Equal("Known Agent", result.GetProperty("name").GetString());
        Assert.Equal(created.Id, result.GetProperty("id").GetString());
    }

    [Fact]
    public async Task ToolCall_GetA2ACard_NoA2AEndpoints_ReturnsError()
    {
        var created = await PostAndDeserialize<AgentResponse>("/agents",
            new RegisterAgentRequest("No A2A", null, null, null,
            [new EndpointRequest("ep", TransportType.Http, ProtocolType.MCP,
                "https://example.com/mcp", LivenessModel.Ephemeral, 300, null, null)]), _admin);

        var (_, sessionId) = await Initialize();
        var result = await CallToolAndParseText(ToolGetA2ACard,
            new { id = created.Id }, sessionId);

        Assert.True(result.TryGetProperty("error", out _));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new { name = "test-client", version = "1.0" }
                }
            }, options: Json)
        };
        request.Headers.Add("Accept", "application/json, text/event-stream");

        var response = await _client.SendAsync(request);
        Assert.True(response.IsSuccessStatusCode,
            $"Initialize failed: {response.StatusCode}");

        var sessionId = response.Headers.TryGetValues("Mcp-Session-Id", out var vals)
            ? vals.FirstOrDefault()
            : null;

        var text = await response.Content.ReadAsStringAsync();
        var json = ExtractJsonFromSse(text);
        var root = JsonDocument.Parse(json).RootElement;

        // The data payload may or may not include the jsonrpc envelope.
        var result = root.TryGetProperty("result", out var r) ? r : root;

        await SendNotification("notifications/initialized", new { }, sessionId);

        return (result, sessionId);
    }

    private async Task SendNotification(string method, object @params, string? sessionId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", method, @params }, options: Json)
        };
        request.Headers.Add("Accept", "application/json, text/event-stream");
        if (sessionId is not null) request.Headers.Add("Mcp-Session-Id", sessionId);
        await _client.SendAsync(request);
    }

    private async Task<JsonElement> CallMethod(string method, object @params, string? sessionId)
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
        request.Headers.Add("Accept", "application/json, text/event-stream");
        if (sessionId is not null) request.Headers.Add("Mcp-Session-Id", sessionId);

        var response = await _client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode,
            $"MCP {method} returned {response.StatusCode}. Body: {text[..Math.Min(300, text.Length)]}");

        var json = ExtractJsonFromSse(text);
        var root = JsonDocument.Parse(json).RootElement;

        // Return result if present, otherwise the root (let callers handle errors).
        return root.TryGetProperty("result", out var result) ? result : root;
    }

    private async Task<JsonElement> CallToolAndParseText(string toolName, object arguments, string? sessionId)
    {
        var result = await CallMethod("tools/call", new { name = toolName, arguments }, sessionId);

        // Result may have content[0].text (success) or be an error payload.
        if (result.TryGetProperty("content", out var content))
        {
            var text = content[0].GetProperty("text").GetString()!;
            return JsonDocument.Parse(text).RootElement;
        }

        // Propagate as-is so callers can assert on error shape.
        return result;
    }

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
