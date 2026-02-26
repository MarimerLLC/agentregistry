using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentRegistry.Api.Agents.Models;
using AgentRegistry.Api.Protocols.ACP.Models;
using AgentRegistry.Api.Tests.Infrastructure;
using AgentRegistry.Domain.Agents;

namespace AgentRegistry.Api.Tests.Protocols.ACP;

public class AcpEndpointTests(AgentRegistryFactory factory) : IClassFixture<AgentRegistryFactory>, IDisposable
{
    private readonly HttpClient _admin = factory.CreateAdminClient();
    private readonly HttpClient _agent = factory.CreateAgentClient();
    private readonly HttpClient _anon = factory.CreateClient();

    public void Dispose() => factory.Reset();

    // ── GET /acp/agents/{id} ──────────────────────────────────────────────────

    [Fact]
    public async Task GetManifest_ForAcpAgent_ReturnsManifest()
    {
        var registered = await RegisterAcpAgent("my-agent");

        var response = await _anon.GetAsync($"/acp/agents/{registered.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var manifest = await response.Content.ReadFromJsonAsync<AcpAgentManifest>();
        Assert.NotNull(manifest);
        Assert.Equal("my-agent", manifest.Name);
        Assert.NotNull(manifest.EndpointUrl);
    }

    [Fact]
    public async Task GetManifest_IsPublic()
    {
        var registered = await RegisterAcpAgent("public-agent");
        var response = await _anon.GetAsync($"/acp/agents/{registered.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetManifest_UnknownId_Returns404()
    {
        var response = await _anon.GetAsync($"/acp/agents/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetManifest_InvalidId_Returns400()
    {
        var response = await _anon.GetAsync("/acp/agents/not-a-guid");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetManifest_AgentWithNoAcpEndpoints_Returns404()
    {
        var req = new RegisterAgentRequest("A2A Only", null, null, null,
            [new EndpointRequest("ep", TransportType.Http, ProtocolType.A2A,
                "https://example.com", LivenessModel.Ephemeral, 300, null, null)]);
        var created = await PostAndDeserialize<AgentResponse>("/agents", req, _admin);

        var response = await _anon.GetAsync($"/acp/agents/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetManifest_NameConvertedToAcpFormat()
    {
        // "My Agent Name" should become "my-agent-name" (RFC 1123 DNS label)
        var req = new RegisterAgentRequest("My Agent Name", null, null, null,
            [new EndpointRequest("ep", TransportType.Http, ProtocolType.ACP,
                "https://example.com/acp", LivenessModel.Persistent, null, 30, null)]);
        var created = await PostAndDeserialize<AgentResponse>("/agents", req, _admin);

        var manifest = await _anon.GetFromJsonAsync<AcpAgentManifest>($"/acp/agents/{created.Id}");
        Assert.NotNull(manifest);
        Assert.Equal("my-agent-name", manifest.Name);
    }

    [Fact]
    public async Task GetManifest_CapabilitiesMappedFromDomain()
    {
        var req = new RegisterAgentRequest("capable-agent", "Has skills", null,
            [
                new CapabilityRequest("summarize", "Summarizes documents", ["nlp"]),
                new CapabilityRequest("classify", "Classifies text", ["nlp", "ml"]),
            ],
            [new EndpointRequest("ep", TransportType.Http, ProtocolType.ACP,
                "https://example.com/acp", LivenessModel.Persistent, null, 30, null)]);
        var created = await PostAndDeserialize<AgentResponse>("/agents", req, _admin);

        var manifest = await _anon.GetFromJsonAsync<AcpAgentManifest>($"/acp/agents/{created.Id}");
        Assert.NotNull(manifest);
        Assert.NotNull(manifest.Metadata?.Capabilities);
        Assert.Equal(2, manifest.Metadata!.Capabilities!.Count);

        var capNames = manifest.Metadata.Capabilities.Select(c => c.Name).ToHashSet();
        Assert.Contains("summarize", capNames);
        Assert.Contains("classify", capNames);
    }

    [Fact]
    public async Task GetManifest_IncludesLivenessStatus()
    {
        var registered = await RegisterAcpAgent("live-check");
        var manifest = await _anon.GetFromJsonAsync<AcpAgentManifest>($"/acp/agents/{registered.Id}");
        Assert.NotNull(manifest);
        Assert.True(manifest.IsLive);
    }

    // ── GET /acp/agents ───────────────────────────────────────────────────────

    [Fact]
    public async Task ListAgents_IsPublic()
    {
        var response = await _anon.GetAsync("/acp/agents");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ListAgents_ReturnsOnlyAcpAgents()
    {
        await RegisterAcpAgent("acp-1");
        await RegisterAcpAgent("acp-2");

        // Register non-ACP agent — should not appear.
        await _admin.PostAsJsonAsync("/agents", new RegisterAgentRequest(
            "MCP Agent", null, null, null,
            [new EndpointRequest("ep", TransportType.Http, ProtocolType.MCP,
                "https://example.com/mcp", LivenessModel.Ephemeral, 300, null, null)]));

        var result = await _anon.GetFromJsonAsync<JsonDocument>("/acp/agents?liveOnly=true");
        Assert.NotNull(result);

        var agents = result.RootElement.GetProperty("agents").EnumerateArray().ToList();
        Assert.Equal(2, agents.Count);
    }

    [Fact]
    public async Task ListAgents_PaginationWorks()
    {
        for (var i = 0; i < 5; i++)
            await RegisterAcpAgent($"agent-{i:D2}");

        var result = await _anon.GetFromJsonAsync<JsonDocument>("/acp/agents?liveOnly=true&page=1&pageSize=3");
        Assert.NotNull(result);

        var agents = result.RootElement.GetProperty("agents").EnumerateArray().ToList();
        Assert.Equal(3, agents.Count);
        Assert.Equal(5, result.RootElement.GetProperty("totalCount").GetInt32());
    }

    // ── POST /acp/agents ──────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterViaManifest_AsAgent_Returns201()
    {
        var request = new
        {
            manifest = BuildSampleManifest("native-agent"),
            endpointUrl = "https://native.example.com/acp"
        };

        var response = await _agent.PostAsJsonAsync("/acp/agents", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var manifest = await response.Content.ReadFromJsonAsync<AcpAgentManifest>();
        Assert.NotNull(manifest);
        Assert.Equal("native-agent", manifest.Name);
    }

    [Fact]
    public async Task RegisterViaManifest_Unauthenticated_Returns401()
    {
        var response = await _anon.PostAsJsonAsync("/acp/agents", new
        {
            manifest = BuildSampleManifest("unauth"),
            endpointUrl = "https://example.com/acp"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RegisterViaManifest_MissingEndpointUrl_Returns400()
    {
        var response = await _agent.PostAsJsonAsync("/acp/agents", new
        {
            manifest = BuildSampleManifest("no-url"),
            endpointUrl = ""
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterViaManifest_ContentTypesRoundTrip()
    {
        var manifest = BuildSampleManifest("content-types") with
        {
            InputContentTypes = ["text/plain", "image/png"],
            OutputContentTypes = ["application/json"]
        };

        var createResponse = await _agent.PostAsJsonAsync("/acp/agents", new
        {
            manifest,
            endpointUrl = "https://example.com/acp"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<AcpAgentManifest>();
        var fetched = await _anon.GetFromJsonAsync<AcpAgentManifest>($"/acp/agents/{created!.Id}");

        Assert.NotNull(fetched);
        Assert.Equal(["text/plain", "image/png"], fetched.InputContentTypes);
        Assert.Equal(["application/json"], fetched.OutputContentTypes);
    }

    [Fact]
    public async Task RegisterViaManifest_MetadataRoundTrips()
    {
        var manifest = BuildSampleManifest("full-metadata") with
        {
            Metadata = new AcpMetadata
            {
                Capabilities = [new AcpCapability { Name = "summarize", Description = "Summarizes text" }],
                Tags = ["nlp", "text"],
                Domains = ["document-processing"],
                Framework = "LangChain",
                License = "MIT",
                Author = new AcpAuthor { Name = "Acme Corp", Url = "https://acme.example.com" }
            }
        };

        var createResponse = await _agent.PostAsJsonAsync("/acp/agents", new
        {
            manifest,
            endpointUrl = "https://example.com/acp"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<AcpAgentManifest>();
        var fetched = await _anon.GetFromJsonAsync<AcpAgentManifest>($"/acp/agents/{created!.Id}");

        Assert.NotNull(fetched?.Metadata);
        Assert.Equal("LangChain", fetched.Metadata!.Framework);
        Assert.Equal("MIT", fetched.Metadata.License);
        Assert.Equal("Acme Corp", fetched.Metadata.Author?.Name);
    }

    [Fact]
    public async Task RegisterViaManifest_StatusRoundTrips()
    {
        var manifest = BuildSampleManifest("with-status") with
        {
            Status = new AcpStatus { AvgRunTimeSeconds = 1.5, SuccessRate = 0.98 }
        };

        var createResponse = await _agent.PostAsJsonAsync("/acp/agents", new
        {
            manifest,
            endpointUrl = "https://example.com/acp"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<AcpAgentManifest>();
        var fetched = await _anon.GetFromJsonAsync<AcpAgentManifest>($"/acp/agents/{created!.Id}");

        Assert.NotNull(fetched?.Status);
        Assert.Equal(1.5, fetched.Status!.AvgRunTimeSeconds);
        Assert.Equal(0.98, fetched.Status.SuccessRate);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<AgentResponse> RegisterAcpAgent(string name,
        string url = "https://acp.example.com/acp")
    {
        var req = new RegisterAgentRequest(name, null, null, null,
            [new EndpointRequest("acp-http", TransportType.Http, ProtocolType.ACP,
                url, LivenessModel.Persistent, null, 30, null)]);
        return await PostAndDeserialize<AgentResponse>("/agents", req, _admin);
    }

    private static AcpAgentManifest BuildSampleManifest(string name) => new()
    {
        Name = name,
        Description = $"{name} description",
        InputContentTypes = ["text/plain", "application/json"],
        OutputContentTypes = ["text/plain"],
        Metadata = new AcpMetadata
        {
            Capabilities = [new AcpCapability { Name = "process", Description = "Processes input" }],
            Tags = ["acp", "sample"],
        }
    };

    private static async Task<T> PostAndDeserialize<T>(string url, object body, HttpClient client)
    {
        var response = await client.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
