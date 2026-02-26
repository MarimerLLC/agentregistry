using System.Net;
using System.Net.Http.Json;
using MarimerLLC.AgentRegistry.Api.Agents.Models;
using MarimerLLC.AgentRegistry.Api.Protocols.A2A.Models;
using MarimerLLC.AgentRegistry.Api.Tests.Infrastructure;
using MarimerLLC.AgentRegistry.Domain.Agents;

namespace MarimerLLC.AgentRegistry.Api.Tests.Protocols.A2A;

public class A2AEndpointTests(AgentRegistryFactory factory) : IClassFixture<AgentRegistryFactory>, IDisposable
{
    private readonly HttpClient _admin = factory.CreateAdminClient();
    private readonly HttpClient _agent = factory.CreateAgentClient();
    private readonly HttpClient _anon = factory.CreateClient();

    public void Dispose() => factory.Reset();

    // ── GET /.well-known/agent.json ───────────────────────────────────────────

    [Fact]
    public async Task WellKnown_ReturnsRegistryCard()
    {
        var response = await _anon.GetAsync("/.well-known/agent.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var card = await response.Content.ReadFromJsonAsync<AgentCard>();
        Assert.NotNull(card);
        Assert.Equal("AgentRegistry", card.Name);
        Assert.NotEmpty(card.Skills);
        Assert.NotEmpty(card.SupportedInterfaces);
    }

    [Fact]
    public async Task WellKnown_IsPublic_NoAuthNeeded()
    {
        var response = await _anon.GetAsync("/.well-known/agent.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task WellKnown_SkillsIncludeRegistrationAndDiscovery()
    {
        var card = await _anon.GetFromJsonAsync<AgentCard>("/.well-known/agent.json");
        Assert.NotNull(card);

        var skillIds = card.Skills.Select(s => s.Id).ToHashSet();
        Assert.Contains("agent-registration", skillIds);
        Assert.Contains("agent-discovery", skillIds);
    }

    // ── GET /a2a/agents/{id} ──────────────────────────────────────────────────

    [Fact]
    public async Task GetAgentCard_ForA2AAgent_ReturnsCard()
    {
        var registered = await RegisterA2AAgent("My A2A Agent");

        var response = await _anon.GetAsync($"/a2a/agents/{registered.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var card = await response.Content.ReadFromJsonAsync<AgentCard>();
        Assert.NotNull(card);
        Assert.Equal("My A2A Agent", card.Name);
        Assert.NotEmpty(card.SupportedInterfaces);
    }

    [Fact]
    public async Task GetAgentCard_IsPublic_NoAuthNeeded()
    {
        var registered = await RegisterA2AAgent("Public Card Test");

        var response = await _anon.GetAsync($"/a2a/agents/{registered.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetAgentCard_UnknownAgent_Returns404()
    {
        var response = await _anon.GetAsync($"/a2a/agents/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAgentCard_InvalidId_Returns400()
    {
        var response = await _anon.GetAsync("/a2a/agents/not-a-guid");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAgentCard_AgentWithNoA2AEndpoints_Returns404()
    {
        // Register a generic agent with an MCP endpoint — no A2A endpoint.
        var req = new RegisterAgentRequest(
            "MCP Only Agent", null, null,
            [new CapabilityRequest("translate", null, null)],
            [new EndpointRequest("primary", TransportType.Http, ProtocolType.MCP,
                "https://example.com/mcp", LivenessModel.Persistent, null, 30, null)]);

        var created = await PostAndDeserialize<AgentResponse>("/agents", req, _admin);

        var response = await _anon.GetAsync($"/a2a/agents/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAgentCard_CapabilitiesMappedToSkills()
    {
        var req = new RegisterAgentRequest(
            "Skilled Agent", "Has skills", null,
            [
                new CapabilityRequest("summarize", "Summarizes text", ["nlp", "text"]),
                new CapabilityRequest("translate", "Translates text", ["nlp", "language"]),
            ],
            [new EndpointRequest("primary", TransportType.Http, ProtocolType.A2A,
                "https://agent.example.com", LivenessModel.Ephemeral, 300, null, null)]);

        var created = await PostAndDeserialize<AgentResponse>("/agents", req, _admin);
        var card = await _anon.GetFromJsonAsync<AgentCard>($"/a2a/agents/{created.Id}");

        Assert.NotNull(card);
        Assert.Equal(2, card.Skills.Count);
        var skillNames = card.Skills.Select(s => s.Name).ToHashSet();
        Assert.Contains("summarize", skillNames);
        Assert.Contains("translate", skillNames);
    }

    // ── POST /a2a/agents ──────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterViaCard_AsAgent_Returns201()
    {
        var card = BuildSampleCard("Native A2A Agent");

        var response = await _agent.PostAsJsonAsync("/a2a/agents", new { card });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var returned = await response.Content.ReadFromJsonAsync<AgentCard>();
        Assert.NotNull(returned);
        Assert.Equal("Native A2A Agent", returned.Name);
    }

    [Fact]
    public async Task RegisterViaCard_Unauthenticated_Returns401()
    {
        var response = await _anon.PostAsJsonAsync("/a2a/agents",
            new { card = BuildSampleCard("Unauth") });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RegisterViaCard_SkillsRoundTrip()
    {
        var card = BuildSampleCard("Round Trip Agent");

        var createResponse = await _agent.PostAsJsonAsync("/a2a/agents", new { card });
        var created = await createResponse.Content.ReadFromJsonAsync<AgentCard>();
        Assert.NotNull(created);
        Assert.NotNull(created.Id);

        // Fetch via the agent card endpoint and verify skills round-tripped.
        var fetchedCard = await _anon.GetFromJsonAsync<AgentCard>($"/a2a/agents/{created.Id}");
        Assert.NotNull(fetchedCard);

        var skillNames = fetchedCard.Skills.Select(s => s.Name).ToHashSet();
        Assert.Contains("Summarize", skillNames);
        Assert.Contains("Translate", skillNames);
    }

    [Fact]
    public async Task RegisterViaCard_CapabilitiesPreserved()
    {
        var card = BuildSampleCard("Streaming Agent") with
        {
            Capabilities = new AgentCapabilities { Streaming = true, PushNotifications = true }
        };

        var createResponse = await _agent.PostAsJsonAsync("/a2a/agents", new { card });
        var created = await createResponse.Content.ReadFromJsonAsync<AgentCard>();
        var fetched = await _anon.GetFromJsonAsync<AgentCard>($"/a2a/agents/{created!.Id}");

        Assert.NotNull(fetched);
        Assert.True(fetched.Capabilities.Streaming);
        Assert.True(fetched.Capabilities.PushNotifications);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<AgentResponse> RegisterA2AAgent(string name)
    {
        var req = new RegisterAgentRequest(name, null, null, null,
            [new EndpointRequest("primary", TransportType.Http, ProtocolType.A2A,
                "https://agent.example.com", LivenessModel.Ephemeral, 300, null, null)]);
        return await PostAndDeserialize<AgentResponse>("/agents", req, _admin);
    }

    private static AgentCard BuildSampleCard(string name) => new()
    {
        Name = name,
        Description = $"{name} description",
        Version = "2.1.0",
        Capabilities = new AgentCapabilities { Streaming = false },
        Skills =
        [
            new AgentSkill { Id = "summarize", Name = "Summarize", Description = "Summarizes text", Tags = ["nlp"] },
            new AgentSkill { Id = "translate", Name = "Translate", Description = "Translates text", Tags = ["nlp", "language"] },
        ],
        SupportedInterfaces =
        [
            new AgentInterface { Url = "https://agent.example.com/a2a", Transport = "JSONRPC" }
        ],
        DefaultInputModes = ["text/plain"],
        DefaultOutputModes = ["text/plain"],
    };

    private static async Task<T> PostAndDeserialize<T>(string url, object body, HttpClient client)
    {
        var response = await client.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
