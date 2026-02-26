using System.Net;
using System.Net.Http.Json;
using MarimerLLC.AgentRegistry.Api.Agents.Models;
using MarimerLLC.AgentRegistry.Api.Tests.Infrastructure;
using MarimerLLC.AgentRegistry.Domain.Agents;

namespace MarimerLLC.AgentRegistry.Api.Tests.Agents;

public class RegistrationTests(AgentRegistryFactory factory) : IClassFixture<AgentRegistryFactory>, IDisposable
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    public void Dispose() => factory.Reset();

    // ── POST /agents ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Register_WithValidRequest_Returns201()
    {
        var request = new RegisterAgentRequest(
            Name: "Test Agent",
            Description: "A test agent",
            Labels: new Dictionary<string, string> { ["env"] = "test" },
            Capabilities: [new CapabilityRequest("summarize", "Summarizes text", ["nlp"])],
            Endpoints: [new EndpointRequest(
                "primary", TransportType.Http, ProtocolType.A2A,
                "https://example.com/agent", LivenessModel.Ephemeral,
                TtlSeconds: 300, HeartbeatIntervalSeconds: null, ProtocolMetadata: null)]);

        var response = await _client.PostAsJsonAsync("/agents", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
    }

    [Fact]
    public async Task Register_ReturnsAgentWithEndpointsAndCapabilities()
    {
        var request = new RegisterAgentRequest(
            Name: "Full Agent",
            Description: null,
            Labels: null,
            Capabilities: [new CapabilityRequest("translate", null, ["nlp", "language"])],
            Endpoints: [new EndpointRequest(
                "queue", TransportType.AzureServiceBus, ProtocolType.A2A,
                "my-queue", LivenessModel.Ephemeral,
                TtlSeconds: 60, HeartbeatIntervalSeconds: null, ProtocolMetadata: null)]);

        var response = await _client.PostAsJsonAsync("/agents", request);
        var body = await response.Content.ReadFromJsonAsync<AgentResponse>();

        Assert.NotNull(body);
        Assert.Equal("Full Agent", body.Name);
        Assert.Single(body.Capabilities);
        Assert.Single(body.Endpoints);
        Assert.Equal("AzureServiceBus", body.Endpoints[0].Transport);
    }

    [Fact]
    public async Task Register_WithoutAuth_Returns401()
    {
        var unauthClient = factory.CreateClient();
        var response = await unauthClient.PostAsJsonAsync("/agents",
            new RegisterAgentRequest("X", null, null, null, null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── GET /agents/{id} ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_ExistingAgent_ReturnsAgent()
    {
        var created = await RegisterAgent("Get Test");

        var response = await _client.GetAsync($"/agents/{created.Id}");
        var body = await response.Content.ReadFromJsonAsync<AgentResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Get Test", body!.Name);
    }

    [Fact]
    public async Task GetById_UnknownId_Returns404()
    {
        var response = await _client.GetAsync($"/agents/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetById_InvalidGuid_Returns400()
    {
        var response = await _client.GetAsync("/agents/not-a-guid");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── PUT /agents/{id} ─────────────────────────────────────────────────────

    [Fact]
    public async Task Update_OwnedAgent_Returns200WithNewName()
    {
        var created = await RegisterAgent("Original Name");

        var response = await _client.PutAsJsonAsync($"/agents/{created.Id}",
            new UpdateAgentRequest("Updated Name", "new desc", null));
        var body = await response.Content.ReadFromJsonAsync<AgentResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Updated Name", body!.Name);
    }

    [Fact]
    public async Task Update_UnknownAgent_Returns404()
    {
        var response = await _client.PutAsJsonAsync($"/agents/{Guid.NewGuid()}",
            new UpdateAgentRequest("X", null, null));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── DELETE /agents/{id} ──────────────────────────────────────────────────

    [Fact]
    public async Task Deregister_OwnedAgent_Returns204()
    {
        var created = await RegisterAgent("To Delete");

        var deleteResponse = await _client.DeleteAsync($"/agents/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await _client.GetAsync($"/agents/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    // ── Endpoint management ───────────────────────────────────────────────────

    [Fact]
    public async Task AddEndpoint_ToOwnedAgent_Returns201()
    {
        var created = await RegisterAgent("Endpoint Test");

        var response = await _client.PostAsJsonAsync($"/agents/{created.Id}/endpoints",
            new EndpointRequest(
                "secondary", TransportType.Http, ProtocolType.MCP,
                "https://example.com/mcp", LivenessModel.Persistent,
                TtlSeconds: null, HeartbeatIntervalSeconds: 30, ProtocolMetadata: null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task RemoveEndpoint_ExistingEndpoint_Returns204()
    {
        var request = new RegisterAgentRequest("Ep Remove", null, null, null,
            [new EndpointRequest("ep", TransportType.Http, ProtocolType.A2A,
                "https://example.com", LivenessModel.Ephemeral, 300, null, null)]);
        var created = await PostAndDeserialize<AgentResponse>("/agents", request);

        var endpointId = created.Endpoints[0].Id;
        var response = await _client.DeleteAsync($"/agents/{created.Id}/endpoints/{endpointId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ── Liveness ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Heartbeat_PersistentEndpoint_Returns204()
    {
        var request = new RegisterAgentRequest("Heartbeat Agent", null, null, null,
            [new EndpointRequest("ep", TransportType.Http, ProtocolType.MCP,
                "https://example.com", LivenessModel.Persistent, null, 30, null)]);
        var created = await PostAndDeserialize<AgentResponse>("/agents", request);
        var endpointId = created.Endpoints[0].Id;

        var response = await _client.PostAsync(
            $"/agents/{created.Id}/endpoints/{endpointId}/heartbeat", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Renew_EphemeralEndpoint_Returns204()
    {
        var request = new RegisterAgentRequest("Renew Agent", null, null, null,
            [new EndpointRequest("ep", TransportType.AzureServiceBus, ProtocolType.A2A,
                "my-queue", LivenessModel.Ephemeral, 300, null, null)]);
        var created = await PostAndDeserialize<AgentResponse>("/agents", request);
        var endpointId = created.Endpoints[0].Id;

        var response = await _client.PostAsync(
            $"/agents/{created.Id}/endpoints/{endpointId}/renew", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Heartbeat_OnEphemeralEndpoint_Returns400()
    {
        var request = new RegisterAgentRequest("Bad HB", null, null, null,
            [new EndpointRequest("ep", TransportType.Http, ProtocolType.A2A,
                "https://example.com", LivenessModel.Ephemeral, 300, null, null)]);
        var created = await PostAndDeserialize<AgentResponse>("/agents", request);
        var endpointId = created.Endpoints[0].Id;

        var response = await _client.PostAsync(
            $"/agents/{created.Id}/endpoints/{endpointId}/heartbeat", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<AgentResponse> RegisterAgent(string name)
    {
        var request = new RegisterAgentRequest(name, null, null, null, null);
        return await PostAndDeserialize<AgentResponse>("/agents", request);
    }

    private async Task<T> PostAndDeserialize<T>(string url, object body)
    {
        var response = await _client.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
