using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MarimerLLC.AgentRegistry.Api.Agents.Models;
using MarimerLLC.AgentRegistry.Api.Protocols.MCP.Models;
using MarimerLLC.AgentRegistry.Api.Tests.Infrastructure;
using MarimerLLC.AgentRegistry.Domain.Agents;

namespace MarimerLLC.AgentRegistry.Api.Tests.Protocols.MCP;

public class McpEndpointTests(AgentRegistryFactory factory) : IClassFixture<AgentRegistryFactory>, IDisposable
{
    private readonly HttpClient _admin = factory.CreateAdminClient();
    private readonly HttpClient _agent = factory.CreateAgentClient();
    private readonly HttpClient _anon = factory.CreateClient();

    public void Dispose() => factory.Reset();

    // ── GET /mcp/servers/{id} ─────────────────────────────────────────────────

    [Fact]
    public async Task GetServerCard_ForMcpServer_ReturnsCard()
    {
        var registered = await RegisterMcpServer("My MCP Server");

        var response = await _anon.GetAsync($"/mcp/servers/{registered.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var card = await response.Content.ReadFromJsonAsync<McpServerCard>();
        Assert.NotNull(card);
        Assert.Equal("My MCP Server", card.ServerInfo.Name);
        Assert.NotNull(card.Endpoints.StreamableHttp);
    }

    [Fact]
    public async Task GetServerCard_IsPublic()
    {
        var registered = await RegisterMcpServer("Public Server");
        var response = await _anon.GetAsync($"/mcp/servers/{registered.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetServerCard_UnknownId_Returns404()
    {
        var response = await _anon.GetAsync($"/mcp/servers/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetServerCard_InvalidId_Returns400()
    {
        var response = await _anon.GetAsync("/mcp/servers/not-a-guid");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetServerCard_AgentWithNoMcpEndpoints_Returns404()
    {
        var req = new RegisterAgentRequest("A2A Only", null, null, null,
            [new EndpointRequest("ep", TransportType.Http, ProtocolType.A2A,
                "https://example.com", LivenessModel.Ephemeral, 300, null, null)]);
        var created = await PostAndDeserialize<AgentResponse>("/agents", req, _admin);

        var response = await _anon.GetAsync($"/mcp/servers/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetServerCard_EndpointUrlPreserved()
    {
        var registered = await RegisterMcpServer("URL Test", "https://my-server.example.com/mcp");

        var card = await _anon.GetFromJsonAsync<McpServerCard>($"/mcp/servers/{registered.Id}");
        Assert.NotNull(card);
        Assert.Equal("https://my-server.example.com/mcp", card.Endpoints.StreamableHttp);
    }

    [Fact]
    public async Task GetServerCard_IncludesLivenessStatus()
    {
        var registered = await RegisterMcpServer("Live Check");

        var card = await _anon.GetFromJsonAsync<McpServerCard>($"/mcp/servers/{registered.Id}");
        Assert.NotNull(card);
        // Ephemeral endpoint with 300s TTL — set alive on registration, should be live.
        Assert.True(card.IsLive);
    }

    // ── GET /mcp/servers ──────────────────────────────────────────────────────

    [Fact]
    public async Task ListServers_IsPublic()
    {
        var response = await _anon.GetAsync("/mcp/servers");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ListServers_ReturnsOnlyMcpServers()
    {
        await RegisterMcpServer("MCP Server 1");
        await RegisterMcpServer("MCP Server 2");

        // Register a non-MCP agent — should not appear.
        await _admin.PostAsJsonAsync("/agents", new RegisterAgentRequest(
            "A2A Agent", null, null, null,
            [new EndpointRequest("ep", TransportType.Http, ProtocolType.A2A,
                "https://example.com", LivenessModel.Ephemeral, 300, null, null)]));

        var result = await _anon.GetFromJsonAsync<JsonDocument>("/mcp/servers?liveOnly=true");
        Assert.NotNull(result);

        var items = result.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.All(items, item =>
            Assert.NotNull(item.GetProperty("endpoints").GetProperty("streamableHttp").GetString()));
    }

    [Fact]
    public async Task ListServers_PaginationWorks()
    {
        for (var i = 0; i < 4; i++)
            await RegisterMcpServer($"Server {i:D2}");

        var result = await _anon.GetFromJsonAsync<JsonDocument>("/mcp/servers?liveOnly=true&page=1&pageSize=2");
        Assert.NotNull(result);

        var items = result.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
    }

    // ── POST /mcp/servers ─────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterViaCard_AsAgent_Returns201()
    {
        var card = BuildSampleCard("Native MCP Server");

        var response = await _agent.PostAsJsonAsync("/mcp/servers",
            new { serverCard = card });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var returned = await response.Content.ReadFromJsonAsync<McpServerCard>();
        Assert.NotNull(returned);
        Assert.Equal("Native MCP Server", returned.ServerInfo.Name);
    }

    [Fact]
    public async Task RegisterViaCard_Unauthenticated_Returns401()
    {
        var response = await _anon.PostAsJsonAsync("/mcp/servers",
            new { serverCard = BuildSampleCard("Unauth") });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RegisterViaCard_MissingEndpoint_Returns400()
    {
        var card = BuildSampleCard("No Endpoint") with
        {
            Endpoints = new McpEndpointMap { StreamableHttp = null }
        };

        var response = await _agent.PostAsJsonAsync("/mcp/servers",
            new { serverCard = card });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterViaCard_ToolsRoundTrip()
    {
        var card = BuildSampleCard("Tool Server");

        var createResponse = await _agent.PostAsJsonAsync("/mcp/servers", new { serverCard = card });
        var created = await createResponse.Content.ReadFromJsonAsync<McpServerCard>();
        Assert.NotNull(created?.Id);

        var fetched = await _anon.GetFromJsonAsync<McpServerCard>($"/mcp/servers/{created.Id}");
        Assert.NotNull(fetched);
        Assert.NotNull(fetched.Tools);
        Assert.Equal(2, fetched.Tools!.Count);

        var toolNames = fetched.Tools.Select(t => t.Name).ToHashSet();
        Assert.Contains("get_weather", toolNames);
        Assert.Contains("search_web", toolNames);
    }

    [Fact]
    public async Task RegisterViaCard_CapabilitiesRoundTrip()
    {
        var card = BuildSampleCard("Cap Server");

        var createResponse = await _agent.PostAsJsonAsync("/mcp/servers", new { serverCard = card });
        var created = await createResponse.Content.ReadFromJsonAsync<McpServerCard>();

        var fetched = await _anon.GetFromJsonAsync<McpServerCard>($"/mcp/servers/{created!.Id}");
        Assert.NotNull(fetched);
        Assert.NotNull(fetched.Capabilities.Tools);
        Assert.True(fetched.Capabilities.Tools!.ListChanged);
        Assert.NotNull(fetched.Capabilities.Resources);
        Assert.True(fetched.Capabilities.Resources!.Subscribe);
    }

    [Fact]
    public async Task RegisterViaCard_McpVersionPreserved()
    {
        var card = BuildSampleCard("Version Test") with { McpVersion = "2025-06-18" };

        var createResponse = await _agent.PostAsJsonAsync("/mcp/servers", new { serverCard = card });
        var created = await createResponse.Content.ReadFromJsonAsync<McpServerCard>();
        var fetched = await _anon.GetFromJsonAsync<McpServerCard>($"/mcp/servers/{created!.Id}");

        Assert.Equal("2025-06-18", fetched!.McpVersion);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<AgentResponse> RegisterMcpServer(string name,
        string url = "https://mcp.example.com/mcp")
    {
        var req = new RegisterAgentRequest(name, null, null, null,
            [new EndpointRequest("streamable-http", TransportType.Http, ProtocolType.MCP,
                url, LivenessModel.Ephemeral, 300, null, null)]);
        return await PostAndDeserialize<AgentResponse>("/agents", req, _admin);
    }

    private static McpServerCard BuildSampleCard(string name) => new()
    {
        McpVersion = "2025-11-25",
        ServerInfo = new McpServerInfo { Name = name, Version = "1.2.0" },
        Endpoints = new McpEndpointMap { StreamableHttp = "https://mcp.example.com/mcp" },
        Capabilities = new McpCapabilities
        {
            Tools = new McpToolsCapability { ListChanged = true },
            Resources = new McpResourcesCapability { Subscribe = true, ListChanged = true },
            Prompts = new McpPromptsCapability { ListChanged = false },
        },
        Tools =
        [
            new McpToolDescriptor { Name = "get_weather", Description = "Gets current weather" },
            new McpToolDescriptor { Name = "search_web", Description = "Searches the web" },
        ],
        Resources =
        [
            new McpResourceDescriptor { Uri = "file:///data/docs", Name = "docs", Description = "Documentation" },
        ],
        Instructions = "Use this server for weather and search tasks.",
    };

    private static async Task<T> PostAndDeserialize<T>(string url, object body, HttpClient client)
    {
        var response = await client.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
