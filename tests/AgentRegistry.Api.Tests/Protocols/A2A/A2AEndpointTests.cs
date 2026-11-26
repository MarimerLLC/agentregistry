using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    // ── GET /.well-known/agent-card.json ──────────────────────────────────────

    [Fact]
    public async Task WellKnown_ReturnsRegistryCard()
    {
        var response = await _anon.GetAsync("/.well-known/agent-card.json");

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
        var response = await _anon.GetAsync("/.well-known/agent-card.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task WellKnown_SkillsIncludeRegistrationAndDiscovery()
    {
        var card = await _anon.GetFromJsonAsync<AgentCard>("/.well-known/agent-card.json");
        Assert.NotNull(card);

        var skillIds = card.Skills.Select(s => s.Id).ToHashSet();
        Assert.Contains("agent-registration", skillIds);
        Assert.Contains("agent-discovery", skillIds);
    }

    [Fact]
    public async Task WellKnown_UsesA2A10Shapes()
    {
        var response = await _anon.GetAsync("/.well-known/agent-card.json");
        var json = (await response.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement;

        Assert.False(json.TryGetProperty("id", out _));
        var iface = json.GetProperty("supportedInterfaces")[0];
        Assert.Equal("JSONRPC", iface.GetProperty("protocolBinding").GetString());
        Assert.Equal("1.0", iface.GetProperty("protocolVersion").GetString());
        Assert.False(iface.TryGetProperty("transport", out _));
        Assert.Equal("Marimer LLC", json.GetProperty("provider").GetProperty("organization").GetString());
        Assert.False(string.IsNullOrEmpty(json.GetProperty("provider").GetProperty("url").GetString()));
    }

    [Fact]
    public async Task WellKnown_SendsCacheAndEtag_And304OnMatch()
    {
        var first = await _anon.GetAsync("/.well-known/agent-card.json");
        Assert.Equal("public, max-age=3600", first.Headers.CacheControl?.ToString());
        Assert.Equal("application/json", first.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(first.Headers.ETag);

        var request = new HttpRequestMessage(HttpMethod.Get, "/.well-known/agent-card.json");
        request.Headers.IfNoneMatch.Add(first.Headers.ETag);
        var second = await _anon.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    [Fact]
    public async Task WellKnown_OldAgentJsonPath_Returns404()
    {
        var response = await _anon.GetAsync("/.well-known/agent.json");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
        var fetchedCard = await RegisterAndFetch(BuildSampleCard("Round Trip Agent"));

        var skills = fetchedCard.Skills.ToDictionary(s => s.Name);
        Assert.Equal(2, skills.Count);
        // The agent's own skill ids survive the round trip.
        Assert.Equal("summarize", skills["Summarize"].Id);
        Assert.Equal("translate", skills["Translate"].Id);
    }

    [Fact]
    public async Task RegisterViaCard_CapabilitiesPreserved()
    {
        var card = BuildSampleCard("Streaming Agent") with
        {
            Capabilities = new AgentCapabilities { Streaming = true, PushNotifications = true }
        };

        var fetched = await RegisterAndFetch(card);

        Assert.True(fetched.Capabilities.Streaming);
        Assert.True(fetched.Capabilities.PushNotifications);
    }

    [Fact]
    public async Task RegisterViaCard_CardHasNoId()
    {
        var response = await _agent.PostAsJsonAsync("/a2a/agents", new { card = BuildSampleCard("No Id") });
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.False(json!.RootElement.TryGetProperty("id", out _));
        Assert.Matches(@"^/a2a/agents/[0-9a-f-]{36}$", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task RegisterViaCard_InterfacesRoundTrip_AllBindings()
    {
        var card = BuildSampleCard("Multi Binding") with
        {
            SupportedInterfaces =
            [
                new AgentInterface { Url = "https://agent.example.com/a2a/jsonrpc", ProtocolBinding = "JSONRPC", ProtocolVersion = "1.0" },
                new AgentInterface { Url = "https://agent.example.com:8443", ProtocolBinding = "GRPC", ProtocolVersion = "1.0", Tenant = "acme" },
                new AgentInterface { Url = "https://agent.example.com/a2a/rest", ProtocolBinding = "HTTP+JSON", ProtocolVersion = "0.3" },
            ]
        };

        var fetched = await RegisterAndFetch(card);

        Assert.Equal(3, fetched.SupportedInterfaces.Count);
        var byBinding = fetched.SupportedInterfaces.ToDictionary(i => i.ProtocolBinding);
        Assert.Equal("https://agent.example.com/a2a/jsonrpc", byBinding["JSONRPC"].Url);
        Assert.Equal("https://agent.example.com:8443", byBinding["GRPC"].Url);
        Assert.Equal("acme", byBinding["GRPC"].Tenant);
        Assert.Equal("0.3", byBinding["HTTP+JSON"].ProtocolVersion);
        Assert.Null(byBinding["JSONRPC"].Tenant);
    }

    [Fact]
    public async Task RegisterViaCard_InterfacesSharingUrl_KeepTheirOwnBindings()
    {
        var card = BuildSampleCard("Shared URL") with
        {
            SupportedInterfaces =
            [
                new AgentInterface { Url = "https://agent.example.com/a2a", ProtocolBinding = "JSONRPC", ProtocolVersion = "1.0" },
                new AgentInterface { Url = "https://agent.example.com/a2a", ProtocolBinding = "HTTP+JSON", ProtocolVersion = "0.3", Tenant = "acme" },
            ]
        };

        var fetched = await RegisterAndFetch(card);

        var byBinding = fetched.SupportedInterfaces.ToDictionary(i => i.ProtocolBinding);
        Assert.Equal(2, byBinding.Count);
        Assert.Equal("1.0", byBinding["JSONRPC"].ProtocolVersion);
        Assert.Null(byBinding["JSONRPC"].Tenant);
        Assert.Equal("0.3", byBinding["HTTP+JSON"].ProtocolVersion);
        Assert.Equal("acme", byBinding["HTTP+JSON"].Tenant);
    }

    [Fact]
    public async Task RegisterViaCard_SkillsSharingName_KeepTheirOwnIds()
    {
        var card = BuildSampleCard("Same Name Skills") with
        {
            Skills =
            [
                new AgentSkill { Id = "a", Name = "Search", Description = "Web", Tags = ["web"], Examples = ["find cats"] },
                new AgentSkill { Id = "b", Name = "Search", Description = "Docs", Tags = ["docs"], Examples = ["find specs"] },
            ]
        };

        var fetched = await RegisterAndFetch(card);

        Assert.Equal(2, fetched.Skills.Count);
        var byId = fetched.Skills.ToDictionary(s => s.Id);
        Assert.Equal("find cats", Assert.Single(byId["a"].Examples!));
        Assert.Equal("find specs", Assert.Single(byId["b"].Examples!));
    }

    [Fact]
    public async Task RegisterViaCard_ExtensionsAndSkillSecurityRoundTrip()
    {
        var card = BuildSampleCard("Secure Agent") with
        {
            Capabilities = new AgentCapabilities
            {
                Extensions =
                [
                    new AgentExtension
                    {
                        Uri = "https://example.com/ext/traceability/v1",
                        Description = "Trace propagation",
                        Required = true,
                        Params = new JsonObject { ["level"] = "full" },
                    },
                ],
            },
            Skills =
            [
                new AgentSkill
                {
                    Id = "summarize", Name = "Summarize", Description = "Summarizes text", Tags = ["nlp"],
                    SecurityRequirements = [new JsonObject { ["oauth"] = new JsonArray("read") }],
                },
            ],
            SecuritySchemes = new JsonObject { ["oauth"] = new JsonObject { ["type"] = "oauth2" } },
        };

        var fetched = await RegisterAndFetch(card);

        var ext = Assert.Single(fetched.Capabilities.Extensions!);
        Assert.Equal("https://example.com/ext/traceability/v1", ext.Uri);
        Assert.True(ext.Required);
        Assert.Equal("full", ext.Params?["level"]?.GetValue<string>());

        var skill = Assert.Single(fetched.Skills);
        var requirement = Assert.Single(skill.SecurityRequirements!);
        Assert.Equal("read", requirement["oauth"]?[0]?.GetValue<string>());
    }

    [Fact]
    public async Task RegisterViaCard_ProviderRoundTrip()
    {
        var card = BuildSampleCard("Provided") with
        {
            Provider = new AgentProvider { Organization = "Example Co", Url = "https://example.com" }
        };

        var fetched = await RegisterAndFetch(card);

        Assert.Equal("Example Co", fetched.Provider?.Organization);
        Assert.Equal("https://example.com", fetched.Provider?.Url);
    }

    [Fact]
    public async Task RegisterViaCard_SignaturesAcceptedButNotReserved()
    {
        var card = BuildSampleCard("Signed") with
        {
            Signatures = [new AgentCardSignature { Protected = "eyJhbGciOiJFUzI1NiJ9", Signature = "c2ln" }]
        };

        var response = await _agent.PostAsJsonAsync("/a2a/agents", new { card });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var fetched = await _anon.GetFromJsonAsync<JsonDocument>(response.Headers.Location!);
        Assert.False(fetched!.RootElement.TryGetProperty("signatures", out _));
    }

    [Fact]
    public async Task RegisterViaCard_OldTransportShape_Returns400()
    {
        // Pre-1.0 interfaces used "transport"; 1.0 requires protocolBinding and protocolVersion.
        var response = await _agent.PostAsJsonAsync("/a2a/agents", new
        {
            card = new
            {
                name = "Old Shape",
                description = "Old",
                version = "1.0",
                capabilities = new { },
                skills = Array.Empty<object>(),
                supportedInterfaces = new[] { new { url = "https://agent.example.com", transport = "JSONRPC" } },
                defaultInputModes = new[] { "text/plain" },
                defaultOutputModes = new[] { "text/plain" },
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAgentCard_GenericAgent_DefaultsToJsonRpc10()
    {
        var registered = await RegisterA2AAgent("Generic");

        var card = await _anon.GetFromJsonAsync<AgentCard>($"/a2a/agents/{registered.Id}");

        var iface = Assert.Single(card!.SupportedInterfaces);
        Assert.Equal("JSONRPC", iface.ProtocolBinding);
        Assert.Equal("1.0", iface.ProtocolVersion);
    }

    [Fact]
    public async Task GetAgentCard_LegacyStoredMetadata_DefaultsInterfacesAndDropsProviderWithoutUrl()
    {
        // A row written before A2A 1.0: no interfaces list, provider without url.
        var legacy = """
            {"version":"0.9","capabilities":{"streaming":true},
             "skills":[{"id":"legacy","name":"Legacy","description":"Old skill","tags":["old"]}],
             "provider":{"organization":"Old Co"}}
            """;
        var req = new RegisterAgentRequest("Legacy Agent", null, null, null,
            [new EndpointRequest("JSONRPC", TransportType.Http, ProtocolType.A2A,
                "https://legacy.example.com", LivenessModel.Ephemeral, 300, null, legacy)]);
        var created = await PostAndDeserialize<AgentResponse>("/agents", req, _admin);

        var card = await _anon.GetFromJsonAsync<AgentCard>($"/a2a/agents/{created.Id}");

        Assert.NotNull(card);
        Assert.Equal("0.9", card.Version);
        Assert.True(card.Capabilities.Streaming);
        Assert.Contains(card.Skills, s => s.Id == "legacy");
        var iface = Assert.Single(card.SupportedInterfaces);
        Assert.Equal("JSONRPC", iface.ProtocolBinding);
        Assert.Equal("1.0", iface.ProtocolVersion);
        Assert.Null(card.Provider);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<AgentCard> RegisterAndFetch(AgentCard card)
    {
        var response = await _agent.PostAsJsonAsync("/a2a/agents", new { card });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var fetched = await _anon.GetFromJsonAsync<AgentCard>(response.Headers.Location!);
        Assert.NotNull(fetched);
        return fetched;
    }

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
            new AgentInterface { Url = "https://agent.example.com/a2a", ProtocolBinding = "JSONRPC", ProtocolVersion = "1.0" }
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
