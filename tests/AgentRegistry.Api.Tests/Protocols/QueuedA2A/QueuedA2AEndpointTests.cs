using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MarimerLLC.AgentRegistry.Api.Agents.Models;
using MarimerLLC.AgentRegistry.Api.Protocols.QueuedA2A.Models;
using MarimerLLC.AgentRegistry.Api.Tests.Infrastructure;
using MarimerLLC.AgentRegistry.Domain.Agents;

namespace MarimerLLC.AgentRegistry.Api.Tests.Protocols.QueuedA2A;

public class QueuedA2AEndpointTests(AgentRegistryFactory factory)
    : IClassFixture<AgentRegistryFactory>, IDisposable
{
    private readonly HttpClient _admin = factory.CreateAdminClient();
    private readonly HttpClient _agent = factory.CreateAgentClient();
    private readonly HttpClient _anon = factory.CreateClient();

    public void Dispose() => factory.Reset();

    // ── POST /a2a/async/agents ────────────────────────────────────────────────

    [Fact]
    public async Task Register_WithRabbitMQ_Returns201_WithId()
    {
        var card = BuildRabbitMqCard("research-agent");

        var response = await _agent.PostAsJsonAsync("/a2a/async/agents", card);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var result = await response.Content.ReadFromJsonAsync<QueuedAgentCard>();
        Assert.NotNull(result);
        Assert.NotNull(result.Id);
        Assert.Equal("research-agent", result.Name);
        Assert.Equal("rabbitmq", result.QueueEndpoint.Technology);
        Assert.Equal("agent.task.ResearchAgent", result.QueueEndpoint.TaskTopic);
    }

    [Fact]
    public async Task Register_WithAzureServiceBus_Returns201_WithId()
    {
        var card = BuildAzureServiceBusCard("azure-agent");

        var response = await _agent.PostAsJsonAsync("/a2a/async/agents", card);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<QueuedAgentCard>();
        Assert.NotNull(result);
        Assert.NotNull(result.Id);
        Assert.Equal("azure-agent", result.Name);
        Assert.Equal("azure-service-bus", result.QueueEndpoint.Technology);
        Assert.Equal("agent-tasks", result.QueueEndpoint.TaskTopic);
    }

    [Fact]
    public async Task Register_MissingTaskTopic_Returns400()
    {
        var card = BuildRabbitMqCard("bad-agent") with
        {
            QueueEndpoint = new QueueEndpoint
            {
                Technology = "rabbitmq",
                Host = "rabbitmq.example.com",
                TaskTopic = "",  // empty — invalid
            }
        };

        var response = await _agent.PostAsJsonAsync("/a2a/async/agents", card);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_Unauthenticated_Returns401()
    {
        var card = BuildRabbitMqCard("unauth-agent");

        var response = await _anon.PostAsJsonAsync("/a2a/async/agents", card);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Register_AsAdminClient_Returns201()
    {
        var card = BuildRabbitMqCard("admin-registered");

        var response = await _admin.PostAsJsonAsync("/a2a/async/agents", card);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ── GET /a2a/async/agents/{id} ────────────────────────────────────────────

    [Fact]
    public async Task GetCard_ReturnsRegisteredCard_WithQueueEndpoint()
    {
        var registered = await RegisterAsync(BuildRabbitMqCard("get-test"));

        var response = await _anon.GetAsync($"/a2a/async/agents/{registered.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var card = await response.Content.ReadFromJsonAsync<QueuedAgentCard>();
        Assert.NotNull(card);
        Assert.Equal("get-test", card.Name);
        Assert.NotNull(card.QueueEndpoint);
        Assert.Equal("rabbitmq", card.QueueEndpoint.Technology);
    }

    [Fact]
    public async Task GetCard_IsPublic()
    {
        var registered = await RegisterAsync(BuildRabbitMqCard("public-card"));
        var response = await _anon.GetAsync($"/a2a/async/agents/{registered.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetCard_UnknownId_Returns404()
    {
        var response = await _anon.GetAsync($"/a2a/async/agents/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetCard_InvalidIdFormat_Returns400()
    {
        var response = await _anon.GetAsync("/a2a/async/agents/not-a-guid");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetCard_AgentWithOnlyHttpEndpoints_Returns404()
    {
        // Register a plain HTTP A2A agent via the generic endpoint — it has no queued endpoints.
        var req = new RegisterAgentRequest("Http A2A Only", null, null, null,
            [new EndpointRequest("ep", TransportType.Http, ProtocolType.A2A,
                "https://example.com/a2a", LivenessModel.Persistent, null, 30, null)]);
        var resp = await _admin.PostAsJsonAsync("/agents", req);
        resp.EnsureSuccessStatusCode();
        var created = await resp.Content.ReadFromJsonAsync<AgentResponse>();

        var response = await _anon.GetAsync($"/a2a/async/agents/{created!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetCard_IncludesLivenessStatus()
    {
        var registered = await RegisterAsync(BuildRabbitMqCard("live-check"));
        var card = await _anon.GetFromJsonAsync<QueuedAgentCard>($"/a2a/async/agents/{registered.Id}");
        Assert.NotNull(card);
        Assert.True(card.IsLive);
    }

    // ── GET /a2a/async/agents ─────────────────────────────────────────────────

    [Fact]
    public async Task ListCards_IsPublic()
    {
        var response = await _anon.GetAsync("/a2a/async/agents");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ListCards_FiltersToQueuedAgents()
    {
        await RegisterAsync(BuildRabbitMqCard("queued-1"));
        await RegisterAsync(BuildRabbitMqCard("queued-2"));

        // Register an HTTP A2A agent — must NOT appear in the queued list.
        var req = new RegisterAgentRequest("Http A2A", null, null, null,
            [new EndpointRequest("ep", TransportType.Http, ProtocolType.A2A,
                "https://example.com/a2a", LivenessModel.Persistent, null, 30, null)]);
        await _admin.PostAsJsonAsync("/agents", req);

        var result = await _anon.GetFromJsonAsync<JsonDocument>("/a2a/async/agents?liveOnly=true");
        Assert.NotNull(result);

        var agents = result.RootElement.GetProperty("agents").EnumerateArray().ToList();
        Assert.Equal(2, agents.Count);
        Assert.All(agents, a => Assert.NotEqual("Http A2A", a.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task ListCards_PaginationWorks()
    {
        for (var i = 0; i < 5; i++)
            await RegisterAsync(BuildRabbitMqCard($"paged-{i:D2}"));

        var result = await _anon.GetFromJsonAsync<JsonDocument>("/a2a/async/agents?liveOnly=true&page=1&pageSize=3");
        Assert.NotNull(result);

        var agents = result.RootElement.GetProperty("agents").EnumerateArray().ToList();
        Assert.Equal(3, agents.Count);
        Assert.Equal(5, result.RootElement.GetProperty("totalCount").GetInt32());
        Assert.True(result.RootElement.GetProperty("hasNextPage").GetBoolean());
    }

    // ── Round-trip ────────────────────────────────────────────────────────────

    [Fact]
    public async Task RoundTrip_AllFields_PreservedOnDiscovery()
    {
        var original = new QueuedAgentCard
        {
            Name = "FullAgent",
            Description = "Full round-trip test agent",
            Version = "2.1",
            Skills =
            [
                new() { Id = "research", Name = "Research", Description = "Web research", Tags = ["search", "web"] },
                new() { Id = "summarize", Name = "Summarize", Description = "Text summarization", Tags = ["nlp"] },
            ],
            DefaultInputModes = ["application/json", "text/plain"],
            DefaultOutputModes = ["application/json"],
            QueueEndpoint = new QueueEndpoint
            {
                Technology = "rabbitmq",
                Host = "rabbitmq.prod.example.com",
                Port = 5672,
                VirtualHost = "/rockbot",
                Exchange = "rockbot",
                TaskTopic = "agent.task.FullAgent",
                ResponseTopic = "agent.response.{callerName}",
            },
        };

        var createResp = await _agent.PostAsJsonAsync("/a2a/async/agents", original);
        createResp.EnsureSuccessStatusCode();
        var created = await createResp.Content.ReadFromJsonAsync<QueuedAgentCard>();

        var fetched = await _anon.GetFromJsonAsync<QueuedAgentCard>($"/a2a/async/agents/{created!.Id}");
        Assert.NotNull(fetched);

        Assert.Equal(original.Name, fetched.Name);
        Assert.Equal(original.Description, fetched.Description);
        Assert.Equal(original.Version, fetched.Version);
        Assert.Equal(original.DefaultInputModes, fetched.DefaultInputModes);
        Assert.Equal(original.DefaultOutputModes, fetched.DefaultOutputModes);

        var q = fetched.QueueEndpoint;
        Assert.Equal("rabbitmq", q.Technology);
        Assert.Equal("rabbitmq.prod.example.com", q.Host);
        Assert.Equal(5672, q.Port);
        Assert.Equal("/rockbot", q.VirtualHost);
        Assert.Equal("rockbot", q.Exchange);
        Assert.Equal("agent.task.FullAgent", q.TaskTopic);
        Assert.Equal("agent.response.{callerName}", q.ResponseTopic);

        Assert.Equal(2, fetched.Skills.Count);
        var skillIds = fetched.Skills.Select(s => s.Id).ToHashSet();
        Assert.Contains("research", skillIds);
        Assert.Contains("summarize", skillIds);
    }

    [Fact]
    public async Task RoundTrip_AzureServiceBus_AllFieldsPreserved()
    {
        var original = BuildAzureServiceBusCard("azure-roundtrip");

        var createResp = await _agent.PostAsJsonAsync("/a2a/async/agents", original);
        createResp.EnsureSuccessStatusCode();
        var created = await createResp.Content.ReadFromJsonAsync<QueuedAgentCard>();

        var fetched = await _anon.GetFromJsonAsync<QueuedAgentCard>($"/a2a/async/agents/{created!.Id}");
        Assert.NotNull(fetched);

        var q = fetched.QueueEndpoint;
        Assert.Equal("azure-service-bus", q.Technology);
        Assert.Equal("mybus.servicebus.windows.net", q.Namespace);
        Assert.Equal("agent-tasks", q.EntityPath);
        Assert.Equal("agent-tasks", q.TaskTopic);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<QueuedAgentCard> RegisterAsync(QueuedAgentCard card)
    {
        var response = await _agent.PostAsJsonAsync("/a2a/async/agents", card);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<QueuedAgentCard>())!;
    }

    private static QueuedAgentCard BuildRabbitMqCard(string name) => new()
    {
        Name = name,
        Description = $"{name} description",
        Version = "1.0",
        Skills =
        [
            new() { Id = "default", Name = name, Description = $"{name} skill", Tags = ["async"] },
        ],
        DefaultInputModes = ["application/json"],
        DefaultOutputModes = ["application/json"],
        QueueEndpoint = new QueueEndpoint
        {
            Technology = "rabbitmq",
            Host = "rabbitmq.example.com",
            Port = 5672,
            VirtualHost = "/",
            Exchange = "rockbot",
            TaskTopic = "agent.task.ResearchAgent",
            ResponseTopic = "agent.response.{callerName}",
        },
    };

    private static QueuedAgentCard BuildAzureServiceBusCard(string name) => new()
    {
        Name = name,
        Description = $"{name} description",
        Version = "1.0",
        Skills =
        [
            new() { Id = "default", Name = name, Description = $"{name} skill", Tags = ["async"] },
        ],
        DefaultInputModes = ["application/json"],
        DefaultOutputModes = ["application/json"],
        QueueEndpoint = new QueueEndpoint
        {
            Technology = "azure-service-bus",
            Namespace = "mybus.servicebus.windows.net",
            EntityPath = "agent-tasks",
            TaskTopic = "agent-tasks",
            ResponseTopic = "agent-response",
        },
    };
}
