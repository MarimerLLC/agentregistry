using System.Net;
using System.Net.Http.Json;
using MarimerLLC.AgentRegistry.Api.Agents.Models;
using MarimerLLC.AgentRegistry.Api.Tests.Infrastructure;
using MarimerLLC.AgentRegistry.Domain.Agents;

namespace MarimerLLC.AgentRegistry.Api.Tests.Agents;

public class DiscoveryTests(AgentRegistryFactory factory) : IClassFixture<AgentRegistryFactory>, IDisposable
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();
    private readonly HttpClient _anonClient = factory.CreateClient();

    public void Dispose() => factory.Reset();

    // ── GET /discover/agents ──────────────────────────────────────────────────

    [Fact]
    public async Task Discover_IsUnauthenticated()
    {
        var response = await _anonClient.GetAsync("/discover/agents");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Discover_ReturnsOnlyLiveAgents_ByDefault()
    {
        // Register agent WITH an endpoint (gets liveness set on registration)
        await RegisterWithEndpoint("Live Agent");
        // Register agent with NO endpoints (nothing in liveness store)
        await Register("No-Endpoint Agent");

        var result = await _anonClient.GetFromJsonAsync<PagedAgentResponse>("/discover/agents?liveOnly=true");

        Assert.NotNull(result);
        // Items contains only live agents; TotalCount reflects the pre-filter DB count.
        Assert.Equal(1, result.Items.Count);
        Assert.Equal("Live Agent", result.Items[0].Name);
    }

    [Fact]
    public async Task Discover_LiveOnlyFalse_ReturnsAll()
    {
        await RegisterWithEndpoint("Agent A");
        await Register("Agent B");

        var result = await _anonClient.GetFromJsonAsync<PagedAgentResponse>("/discover/agents?liveOnly=false");

        Assert.NotNull(result);
        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task Discover_FilterByProtocol_ReturnMatchingOnly()
    {
        await RegisterWithEndpoint("MCP Agent", protocol: ProtocolType.MCP);
        await RegisterWithEndpoint("A2A Agent", protocol: ProtocolType.A2A);

        var result = await _anonClient.GetFromJsonAsync<PagedAgentResponse>("/discover/agents?protocol=MCP&liveOnly=false");

        Assert.NotNull(result);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal("MCP Agent", result.Items[0].Name);
    }

    [Fact]
    public async Task Discover_FilterByTransport_ReturnsMatchingOnly()
    {
        await RegisterWithEndpoint("HTTP Agent", transport: TransportType.Http);
        await RegisterWithEndpoint("Queue Agent", transport: TransportType.AzureServiceBus);

        var result = await _anonClient.GetFromJsonAsync<PagedAgentResponse>(
            "/discover/agents?transport=AzureServiceBus&liveOnly=false");

        Assert.NotNull(result);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Queue Agent", result.Items[0].Name);
    }

    [Fact]
    public async Task Discover_LiveEndpoints_HaveIsLiveTrue()
    {
        await RegisterWithEndpoint("Live Check");

        var result = await _anonClient.GetFromJsonAsync<PagedAgentResponse>("/discover/agents");

        Assert.NotNull(result);
        var agent = result.Items.Single();
        Assert.All(agent.Endpoints, e => Assert.True(e.IsLive));
    }

    [Fact]
    public async Task Discover_Pagination_RespectsPageSize()
    {
        for (var i = 0; i < 5; i++)
            await RegisterWithEndpoint($"Agent {i:D2}");

        var result = await _anonClient.GetFromJsonAsync<PagedAgentResponse>(
            "/discover/agents?liveOnly=true&page=1&pageSize=3");

        Assert.NotNull(result);
        Assert.Equal(3, result.Items.Count);
        Assert.Equal(5, result.TotalCount);
        Assert.True(result.HasNextPage);
    }

    // ── Health checks ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Healthz_ReturnsOk()
    {
        var response = await _anonClient.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readyz_WithFakeInfra_ReturnsUnhealthy()
    {
        // The test host uses dummy connection strings — postgres and redis
        // health checks will fail, which is expected in the isolated test environment.
        var response = await _anonClient.GetAsync("/readyz");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task Register(string name)
    {
        var response = await _client.PostAsJsonAsync("/agents",
            new RegisterAgentRequest(name, null, null, null, null));
        response.EnsureSuccessStatusCode();
    }

    private async Task RegisterWithEndpoint(
        string name,
        ProtocolType protocol = ProtocolType.A2A,
        TransportType transport = TransportType.Http)
    {
        var response = await _client.PostAsJsonAsync("/agents",
            new RegisterAgentRequest(name, null, null, null,
            [new EndpointRequest("primary", transport, protocol,
                "https://example.com", LivenessModel.Ephemeral, 300, null, null)]));
        response.EnsureSuccessStatusCode();
    }
}
