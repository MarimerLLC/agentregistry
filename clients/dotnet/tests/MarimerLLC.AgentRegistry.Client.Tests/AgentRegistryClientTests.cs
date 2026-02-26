using System.Net;
using System.Text;
using System.Text.Json;
using MarimerLLC.AgentRegistry.Client;

namespace MarimerLLC.AgentRegistry.Client.Tests;

public class AgentRegistryClientTests
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static AgentRegistryClient CreateClient(HttpResponseMessage response)
    {
        var handler = new MockHttpMessageHandler(_ => response);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://registry.test") };
        return new AgentRegistryClient(http);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, object body) =>
        new(status)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body, JsonOpts),
                Encoding.UTF8,
                "application/json")
        };

    private static AgentResponse SampleAgent(string? id = null) => new(
        Id: id ?? Guid.NewGuid().ToString(),
        Name: "Test Agent",
        Description: null,
        OwnerId: "owner-1",
        Labels: [],
        Capabilities: [],
        Endpoints: [],
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow);

    // ── RegisterAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterAsync_Returns_AgentResponse()
    {
        var expected = SampleAgent();
        var client = CreateClient(JsonResponse(HttpStatusCode.Created, expected));

        var result = await client.RegisterAsync(new RegisterAgentRequest("Test Agent"));

        Assert.Equal(expected.Id, result.Id);
        Assert.Equal("Test Agent", result.Name);
    }

    [Fact]
    public async Task RegisterAsync_Posts_To_Agents_Endpoint()
    {
        var captured = new List<HttpRequestMessage>();
        var handler = new MockHttpMessageHandler(req =>
        {
            captured.Add(req);
            return JsonResponse(HttpStatusCode.Created, SampleAgent());
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://registry.test") };
        var client = new AgentRegistryClient(http);

        await client.RegisterAsync(new RegisterAgentRequest("My Agent"));

        Assert.Single(captured);
        Assert.Equal(HttpMethod.Post, captured[0].Method);
        Assert.EndsWith("/agents", captured[0].RequestUri!.PathAndQuery);
    }

    // ── GetAgentAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAgentAsync_Returns_Agent_When_Found()
    {
        var expected = SampleAgent();
        var client = CreateClient(JsonResponse(HttpStatusCode.OK, expected));

        var result = await client.GetAgentAsync(Guid.NewGuid());

        Assert.NotNull(result);
        Assert.Equal(expected.Id, result.Id);
    }

    [Fact]
    public async Task GetAgentAsync_Returns_Null_When_NotFound()
    {
        var client = CreateClient(new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await client.GetAgentAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    // ── DiscoverAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DiscoverAsync_Returns_Paged_Response()
    {
        var paged = new PagedAgentResponse([SampleAgent()], 1, 1, 20, 1, false, false);
        var client = CreateClient(JsonResponse(HttpStatusCode.OK, paged));

        var result = await client.DiscoverAsync();

        Assert.Single(result.Items);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task DiscoverAsync_Appends_Filter_QueryString()
    {
        var captured = new List<string>();
        var handler = new MockHttpMessageHandler(req =>
        {
            captured.Add(req.RequestUri!.Query);
            return JsonResponse(HttpStatusCode.OK, new PagedAgentResponse([], 0, 1, 20, 0, false, false));
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://registry.test") };
        var client = new AgentRegistryClient(http);

        await client.DiscoverAsync(new DiscoveryFilter(
            Capability: "summarize",
            Protocol: "MCP",
            LiveOnly: false,
            PageSize: 50));

        Assert.Single(captured);
        Assert.Contains("capability=summarize", captured[0]);
        Assert.Contains("protocol=MCP", captured[0]);
        Assert.Contains("liveOnly=false", captured[0]);
        Assert.Contains("pageSize=50", captured[0]);
    }

    [Fact]
    public async Task DiscoverAsync_Omits_Defaults_From_QueryString()
    {
        var captured = new List<string>();
        var handler = new MockHttpMessageHandler(req =>
        {
            captured.Add(req.RequestUri!.Query);
            return JsonResponse(HttpStatusCode.OK, new PagedAgentResponse([], 0, 1, 20, 0, false, false));
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://registry.test") };
        var client = new AgentRegistryClient(http);

        await client.DiscoverAsync(); // all defaults

        Assert.Single(captured);
        Assert.Equal(string.Empty, captured[0]); // no query string
    }

    // ── HeartbeatAsync / RenewAsync ───────────────────────────────────────────

    [Fact]
    public async Task HeartbeatAsync_Posts_To_Correct_Url()
    {
        var agentId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();
        var captured = new List<HttpRequestMessage>();
        var handler = new MockHttpMessageHandler(req =>
        {
            captured.Add(req);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://registry.test") };
        var client = new AgentRegistryClient(http);

        await client.HeartbeatAsync(agentId, endpointId);

        Assert.Single(captured);
        Assert.Equal(HttpMethod.Post, captured[0].Method);
        Assert.Contains($"/agents/{agentId}/endpoints/{endpointId}/heartbeat",
            captured[0].RequestUri!.PathAndQuery);
    }
}
