using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using MarimerLLC.AgentRegistry.Api.Agents.Models;
using MarimerLLC.AgentRegistry.Api.Protocols.MCP;
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
    public async Task GetServerCard_ForMcpServer_ReturnsSep2127Card()
    {
        var registered = await RegisterMcpServer("My MCP Server");

        var response = await _anon.GetAsync($"/mcp/servers/{registered.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var card = await response.Content.ReadFromJsonAsync<ServerCard>();
        Assert.NotNull(card);
        Assert.Equal(ServerCardConstants.SchemaUrl, card.Schema);
        Assert.Equal("My MCP Server", card.Title);
        var remote = Assert.Single(card.Remotes!);
        Assert.Equal("streamable-http", remote.Type);
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
    public async Task GetServerCard_RemoteUrlPreserved()
    {
        var registered = await RegisterMcpServer("URL Test", "https://my-server.example.com/mcp");

        var card = await _anon.GetFromJsonAsync<ServerCard>($"/mcp/servers/{registered.Id}");
        Assert.NotNull(card);
        Assert.Equal("https://my-server.example.com/mcp", Assert.Single(card.Remotes!).Url);
    }

    [Fact]
    public async Task GetServerCard_RegistryMetaHasIdAndLiveness()
    {
        var registered = await RegisterMcpServer("Live Check");

        var card = await _anon.GetFromJsonAsync<ServerCard>($"/mcp/servers/{registered.Id}");
        var meta = McpServerCardMapper.ReadRegistryMeta(card!);
        Assert.NotNull(meta);
        Assert.Equal(registered.Id, meta.Id);
        // Ephemeral endpoint with 300s TTL — set alive on registration, should be live.
        Assert.True(meta.IsLive);
    }

    [Fact]
    public async Task GetServerCard_GenericAgent_GetsValidDerivedName()
    {
        var registered = await RegisterMcpServer("Generic Agent");

        var card = await _anon.GetFromJsonAsync<ServerCard>($"/mcp/servers/{registered.Id}");
        Assert.NotNull(card);
        Assert.Equal($"agentregistry/{Guid.Parse(registered.Id):N}", card.Name);
        Assert.Matches(ServerCardConstants.NamePattern, card.Name);
        Assert.Equal("1.0.0", card.Version);
        Assert.InRange(card.Description.Length, 1, 100);
    }

    [Fact]
    public async Task GetServerCard_LongDescription_TruncatedTo100()
    {
        var req = new RegisterAgentRequest("Wordy", new string('x', 250), null, null,
            [new EndpointRequest("streamable-http", TransportType.Http, ProtocolType.MCP,
                "https://mcp.example.com/mcp", LivenessModel.Ephemeral, 300, null, null)]);
        var created = await PostAndDeserialize<AgentResponse>("/agents", req, _admin);

        var card = await _anon.GetFromJsonAsync<ServerCard>($"/mcp/servers/{created.Id}");
        Assert.Equal(100, card!.Description.Length);
    }

    [Fact]
    public async Task GetServerCard_LegacyStoredMetadata_StillRead()
    {
        // A row written before SEP-2127 cards: top-level mcpVersion, serverVersion and tools.
        var legacy = """
            {"mcpVersion":"2025-06-18","serverVersion":"3.4.5",
             "tools":[{"name":"old_tool","description":"From an old row"}],
             "instructions":"Old instructions"}
            """;
        var req = new RegisterAgentRequest("Old Server", null, null, null,
            [new EndpointRequest("streamable-http", TransportType.Http, ProtocolType.MCP,
                "https://old.example.com/mcp", LivenessModel.Ephemeral, 300, null, legacy)]);
        var created = await PostAndDeserialize<AgentResponse>("/agents", req, _admin);

        var card = await _anon.GetFromJsonAsync<ServerCard>($"/mcp/servers/{created.Id}");
        Assert.NotNull(card);
        Assert.Equal("3.4.5", card.Version);
        Assert.Equal(["2025-06-18"], Assert.Single(card.Remotes!).SupportedProtocolVersions!);
        var meta = McpServerCardMapper.ReadRegistryMeta(card);
        Assert.Equal("old_tool", Assert.Single(meta!.Tools!).Name);
        Assert.Equal("Old instructions", meta.Instructions);
    }

    [Fact]
    public async Task GetServerCard_LiveRemoteListedFirst()
    {
        var req = new RegisterAgentRequest("Two Remotes", null, null, null,
        [
            new EndpointRequest("first", TransportType.Http, ProtocolType.MCP,
                "https://first.example.com/mcp", LivenessModel.Ephemeral, 300, null, null),
            new EndpointRequest("second", TransportType.Http, ProtocolType.MCP,
                "https://second.example.com/mcp", LivenessModel.Ephemeral, 300, null, null),
        ]);
        var created = await PostAndDeserialize<AgentResponse>("/agents", req, _admin);
        factory.LivenessStore.Clear();
        var second = created.Endpoints.Single(e => e.Name == "second");
        await _admin.PostAsync($"/agents/{created.Id}/endpoints/{second.Id}/renew", null);

        var card = await _anon.GetFromJsonAsync<ServerCard>($"/mcp/servers/{created.Id}");

        Assert.Equal(["https://second.example.com/mcp", "https://first.example.com/mcp"],
            card!.Remotes!.Select(r => r.Url));
    }

    [Fact]
    public async Task GetServerCard_TruncationDoesNotSplitSurrogatePairs()
    {
        // 98 ASCII characters, then emoji: the cut falls inside the first emoji's UTF-16 pair.
        var description = new string('x', 98) + "😀😀😀";
        var req = new RegisterAgentRequest("Emoji", description, null, null,
            [new EndpointRequest("streamable-http", TransportType.Http, ProtocolType.MCP,
                "https://mcp.example.com/mcp", LivenessModel.Ephemeral, 300, null, null)]);
        var created = await PostAndDeserialize<AgentResponse>("/agents", req, _admin);

        var card = await _anon.GetFromJsonAsync<ServerCard>($"/mcp/servers/{created.Id}");

        Assert.Equal(new string('x', 98) + "😀…", card!.Description);
    }

    [Fact]
    public async Task GetServerCard_LegacyAuthenticationCarriedIntoMeta()
    {
        var legacy = """{"mcpVersion":"2025-06-18","authentication":{"type":"oauth2"}}""";
        var req = new RegisterAgentRequest("Old Auth", null, null, null,
            [new EndpointRequest("streamable-http", TransportType.Http, ProtocolType.MCP,
                "https://old.example.com/mcp", LivenessModel.Ephemeral, 300, null, legacy)]);
        var created = await PostAndDeserialize<AgentResponse>("/agents", req, _admin);

        var card = await _anon.GetFromJsonAsync<ServerCard>($"/mcp/servers/{created.Id}");

        var meta = McpServerCardMapper.ReadRegistryMeta(card!);
        Assert.Equal("oauth2", meta!.Authentication?["type"]?.GetValue<string>());
    }

    // ── Media type, CORS, caching ─────────────────────────────────────────────

    [Fact]
    public async Task GetServerCard_AcceptQZero_IsNotSelected()
    {
        var registered = await RegisterMcpServer("Q Zero");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/mcp/servers/{registered.Id}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(ServerCardConstants.MediaType, 0));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var response = await _anon.SendAsync(request);

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetServerCard_AcceptServerCardMediaType_IsEchoed()
    {
        var registered = await RegisterMcpServer("Media Type");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/mcp/servers/{registered.Id}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(ServerCardConstants.MediaType));
        var response = await _anon.SendAsync(request);

        Assert.Equal(ServerCardConstants.MediaType, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetServerCard_NoAccept_IsApplicationJson()
    {
        var registered = await RegisterMcpServer("Plain Json");
        var response = await _anon.GetAsync($"/mcp/servers/{registered.Id}");
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetServerCard_SendsCorsAndCacheHeaders()
    {
        var registered = await RegisterMcpServer("Headers");
        var response = await _anon.GetAsync($"/mcp/servers/{registered.Id}");

        AssertCorsHeaders(response);
        Assert.Equal("public, max-age=60", response.Headers.CacheControl?.ToString());
        Assert.NotNull(response.Headers.ETag);
        Assert.False(response.Headers.ETag.IsWeak);
    }

    [Fact]
    public async Task GetServerCard_IfNoneMatch_Returns304()
    {
        var registered = await RegisterMcpServer("Conditional");
        var first = await _anon.GetAsync($"/mcp/servers/{registered.Id}");
        var etag = first.Headers.ETag!;

        var request = new HttpRequestMessage(HttpMethod.Get, $"/mcp/servers/{registered.Id}");
        request.Headers.IfNoneMatch.Add(etag);
        var second = await _anon.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.Equal(etag, second.Headers.ETag);
        Assert.Empty(await second.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task GetServerCard_IfNoneMatchStale_Returns200()
    {
        var registered = await RegisterMcpServer("Stale");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/mcp/servers/{registered.Id}");
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"stale\""));
        var response = await _anon.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetServerCard_EtagDiffersByMediaType()
    {
        var registered = await RegisterMcpServer("Representations");
        var plain = await _anon.GetAsync($"/mcp/servers/{registered.Id}");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/mcp/servers/{registered.Id}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(ServerCardConstants.MediaType));
        var typed = await _anon.SendAsync(request);

        Assert.NotEqual(plain.Headers.ETag, typed.Headers.ETag);
    }

    [Fact]
    public async Task GetServerCard_Preflight_ReturnsCorsHeaders()
    {
        var registered = await RegisterMcpServer("Preflight");

        var request = new HttpRequestMessage(HttpMethod.Options, $"/mcp/servers/{registered.Id}");
        request.Headers.Add("Origin", "https://browser.example.com");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "If-None-Match");
        var response = await _anon.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        AssertCorsHeaders(response);
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
        {
            Assert.Equal(ServerCardConstants.SchemaUrl, item.GetProperty("$schema").GetString());
            Assert.Equal("streamable-http", item.GetProperty("remotes")[0].GetProperty("type").GetString());
        });
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
        var response = await _agent.PostAsJsonAsync("/mcp/servers", BuildSampleCard("Native MCP Server"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var returned = await response.Content.ReadFromJsonAsync<ServerCard>();
        Assert.NotNull(returned);
        Assert.Equal("Native MCP Server", returned.Title);
        Assert.Equal("com.example/weather", returned.Name);
    }

    [Fact]
    public async Task RegisterViaCard_Unauthenticated_Returns401()
    {
        var response = await _anon.PostAsJsonAsync("/mcp/servers", BuildSampleCard("Unauth"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RegisterViaCard_RemoteUrlRoundTrip()
    {
        var created = await RegisterCard(BuildSampleCard("Remote Server"));

        var fetched = await _anon.GetFromJsonAsync<ServerCard>($"/mcp/servers/{IdOf(created)}");
        var remote = Assert.Single(fetched!.Remotes!);
        Assert.Equal("https://mcp.example.com/mcp", remote.Url);
        Assert.Equal("streamable-http", remote.Type);
        Assert.Equal("Authorization", Assert.Single(remote.Headers!).Name);
        Assert.True(remote.Headers![0].IsSecret);
    }

    [Fact]
    public async Task RegisterViaCard_ToolsInMetaRoundTrip()
    {
        var created = await RegisterCard(BuildSampleCard("Tool Server"));

        var fetched = await _anon.GetFromJsonAsync<ServerCard>($"/mcp/servers/{IdOf(created)}");
        var meta = McpServerCardMapper.ReadRegistryMeta(fetched!);
        Assert.NotNull(meta?.Tools);

        var toolNames = meta.Tools!.Select(t => t.Name).ToHashSet();
        Assert.Equal(["get_weather", "search_web"], toolNames.Order());
        Assert.Equal("object", meta.Tools![0].InputSchema?["type"]?.GetValue<string>());
        Assert.Equal("Use this server for weather and search tasks.", meta.Instructions);
    }

    [Fact]
    public async Task RegisterViaCard_ToolsBecomeSearchableCapabilities()
    {
        var created = await RegisterCard(BuildSampleCard("Searchable Server"));

        var agent = await _admin.GetFromJsonAsync<AgentResponse>($"/agents/{IdOf(created)}");
        var tools = agent!.Capabilities.Where(c => c.Tags.Contains("tool")).Select(c => c.Name).ToHashSet();
        Assert.Contains("get_weather", tools);
        Assert.Contains("search_web", tools);
        Assert.All(agent.Capabilities.Where(c => c.Tags.Contains("tool")), c => Assert.Contains("mcp", c.Tags));
        Assert.Contains(agent.Capabilities, c => c.Name == "docs" && c.Tags.Contains("resource"));

        var byCapability = await _anon.GetFromJsonAsync<JsonDocument>("/mcp/servers?capability=get_weather");
        Assert.Equal(1, byCapability!.RootElement.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task RegisterViaCard_VersionTitleAndProtocolVersionsPreserved()
    {
        var created = await RegisterCard(BuildSampleCard("Version Test"));
        var fetched = await _anon.GetFromJsonAsync<ServerCard>($"/mcp/servers/{IdOf(created)}");

        Assert.Equal("1.2.0", fetched!.Version);
        Assert.Equal("Version Test", fetched.Title);
        Assert.Equal("Weather and web search.", fetched.Description);
        Assert.Equal(["2026-07-28", "2025-11-25"], Assert.Single(fetched.Remotes!).SupportedProtocolVersions!);
        Assert.Equal("https://example.com/weather", fetched.WebsiteUrl);
        Assert.Equal("github", fetched.Repository?.Source);
    }

    [Fact]
    public async Task RegisterViaCard_OtherMetaKeysPreserved_RegistryFieldsRecomputed()
    {
        var card = BuildSampleCard("Meta Keys");
        card.Meta!["com.example/build"] = new JsonObject { ["sha"] = "abc123" };
        // A client cannot claim an id or liveness; the registry sets them.
        card.Meta![ServerCardConstants.RegistryMetaKey]!["id"] = "forged";
        card.Meta![ServerCardConstants.RegistryMetaKey]!["isLive"] = false;

        var created = await RegisterCard(card);
        var fetched = await _anon.GetFromJsonAsync<ServerCard>($"/mcp/servers/{IdOf(created)}");

        Assert.Equal("abc123", fetched!.Meta!["com.example/build"]?["sha"]?.GetValue<string>());
        var meta = McpServerCardMapper.ReadRegistryMeta(fetched)!;
        Assert.NotEqual("forged", meta.Id);
        Assert.True(Guid.TryParse(meta.Id, out _));
        Assert.True(meta.IsLive);
    }

    [Fact]
    public async Task RegisterViaCard_MissingSchema_Returns400()
    {
        var response = await _agent.PostAsJsonAsync("/mcp/servers", BuildSampleCard("No Schema") with { Schema = "" });
        await AssertValidationError(response, "$schema");
    }

    [Fact]
    public async Task RegisterViaCard_WrongSchema_Returns400()
    {
        var card = BuildSampleCard("Old Schema") with
        {
            Schema = "https://static.modelcontextprotocol.io/schemas/2025-12-11/server-card.schema.json"
        };
        var response = await _agent.PostAsJsonAsync("/mcp/servers", card);
        await AssertValidationError(response, "$schema");
    }

    [Theory]
    [InlineData("weather")]               // no slash
    [InlineData("com.example/a/b")]       // two slashes
    [InlineData("com example/x")]         // space
    [InlineData("com.example/weather\n")] // trailing newline (.NET $ would allow it)
    public async Task RegisterViaCard_BadName_Returns400(string name)
    {
        var response = await _agent.PostAsJsonAsync("/mcp/servers", BuildSampleCard("Bad Name") with { Name = name });
        await AssertValidationError(response, "name");
    }

    [Fact]
    public async Task RegisterViaCard_DescriptionTooLong_Returns400()
    {
        var card = BuildSampleCard("Long") with { Description = new string('d', 101) };
        var response = await _agent.PostAsJsonAsync("/mcp/servers", card);
        await AssertValidationError(response, "description");
    }

    [Fact]
    public async Task RegisterViaCard_NoStreamableHttpRemote_Returns400()
    {
        var card = BuildSampleCard("SSE Only") with
        {
            Remotes = [new ServerCardRemote { Type = "sse", Url = "https://mcp.example.com/sse" }]
        };
        var response = await _agent.PostAsJsonAsync("/mcp/servers", card);
        await AssertValidationError(response, "remotes");
    }

    [Theory]
    [InlineData("HTTPS://mcp.example.com/mcp")] // schema's https?:// is case-sensitive
    [InlineData("https://mcp.example.com/m cp")] // whitespace
    [InlineData("ftp://mcp.example.com/mcp")]
    public async Task RegisterViaCard_RemoteUrlNotSchemaValid_Returns400(string url)
    {
        var card = BuildSampleCard("Bad URL") with
        {
            Remotes = [new ServerCardRemote { Type = "streamable-http", Url = url }]
        };
        var response = await _agent.PostAsJsonAsync("/mcp/servers", card);
        await AssertValidationError(response, "remotes");
    }

    [Fact]
    public async Task RegisterViaCard_MalformedRegistryMeta_Returns400()
    {
        var card = BuildSampleCard("Bad Meta");
        card.Meta![ServerCardConstants.RegistryMetaKey] = new JsonObject
        {
            ["tools"] = new JsonArray(new JsonObject { ["description"] = "no name" }),
        };
        var response = await _agent.PostAsJsonAsync("/mcp/servers", card);
        await AssertValidationError(response, "_meta");
    }

    [Fact]
    public async Task RegisterViaCard_RegistryMetaNotAnObject_Returns400()
    {
        var card = BuildSampleCard("Array Meta");
        card.Meta![ServerCardConstants.RegistryMetaKey] = new JsonArray();
        var response = await _agent.PostAsJsonAsync("/mcp/servers", card);
        await AssertValidationError(response, "_meta");
    }

    [Fact]
    public async Task RegisterViaCard_DescriptionCountsCodePoints()
    {
        // 60 emoji = 60 code points (valid), but 120 UTF-16 code units.
        var card = BuildSampleCard("Emoji") with { Description = string.Concat(Enumerable.Repeat("😀", 60)) };
        var response = await _agent.PostAsJsonAsync("/mcp/servers", card);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task RegisterViaCard_SseRemoteIgnored()
    {
        var card = BuildSampleCard("Mixed") with
        {
            Remotes =
            [
                new ServerCardRemote { Type = "sse", Url = "https://mcp.example.com/sse" },
                new ServerCardRemote { Type = "streamable-http", Url = "https://mcp.example.com/mcp" },
            ]
        };
        var created = await RegisterCard(card);

        var remote = Assert.Single(created.Remotes!);
        Assert.Equal("https://mcp.example.com/mcp", remote.Url);
    }

    [Fact]
    public async Task RegisterViaCard_OldWrappedBody_Returns400()
    {
        // The pre-SEP-2127 { serverCard: { mcpVersion, serverInfo, endpoints } } body is rejected.
        var response = await _agent.PostAsJsonAsync("/mcp/servers", new
        {
            serverCard = new
            {
                mcpVersion = "2025-11-25",
                serverInfo = new { name = "Old", version = "1.0.0" },
                endpoints = new { streamableHttp = "https://mcp.example.com/mcp" },
                capabilities = new { },
            }
        });
        await AssertValidationError(response, "$schema");
    }

    // ── GET /mcp/server-card ──────────────────────────────────────────────────

    [Fact]
    public async Task RegistryServerCard_DescribesRegistryMcpEndpoint()
    {
        var response = await _anon.GetAsync("/mcp/server-card");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var card = await response.Content.ReadFromJsonAsync<ServerCard>();
        Assert.NotNull(card);
        Assert.Equal(ServerCardConstants.SchemaUrl, card.Schema);
        Assert.Equal("llc.marimer/agentregistry", card.Name);
        Assert.Equal(RegistryIdentity.Version, card.Version);
        Assert.InRange(card.Description.Length, 1, 100);

        var remote = Assert.Single(card.Remotes!);
        Assert.Equal("streamable-http", remote.Type);
        Assert.Equal("http://localhost/mcp", remote.Url);
        Assert.Equal(["2026-07-28", "2025-11-25"], remote.SupportedProtocolVersions!);

        Assert.Equal("public, max-age=3600", response.Headers.CacheControl?.ToString());
        AssertCorsHeaders(response);
    }

    [Fact]
    public async Task RegistryServerCard_AcceptServerCardMediaType_IsEchoed()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/mcp/server-card");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(ServerCardConstants.MediaType));
        var response = await _anon.SendAsync(request);

        Assert.Equal(ServerCardConstants.MediaType, response.Content.Headers.ContentType?.MediaType);
    }

    // ── GET /.well-known/ai-catalog.json ──────────────────────────────────────

    [Fact]
    public async Task AiCatalog_ListsRegistryCardThenLiveMcpServers()
    {
        var live = await RegisterMcpServer("Live Server");
        var dead = await RegisterMcpServer("Dead Server");
        factory.LivenessStore.Clear();
        await _admin.PostAsync($"/agents/{live.Id}/endpoints/{live.Endpoints[0].Id}/renew", null);

        // A2A agents are not MCP servers and are not listed.
        await _admin.PostAsJsonAsync("/agents", new RegisterAgentRequest(
            "A2A Agent", null, null, null,
            [new EndpointRequest("ep", TransportType.Http, ProtocolType.A2A,
                "https://example.com", LivenessModel.Ephemeral, 300, null, null)]));

        var response = await _anon.GetAsync("/.well-known/ai-catalog.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ServerCardConstants.AiCatalogMediaType, response.Content.Headers.ContentType?.MediaType);
        AssertCorsHeaders(response);
        Assert.NotNull(response.Headers.ETag);

        var catalog = await response.Content.ReadFromJsonAsync<AiCatalog>();
        Assert.NotNull(catalog);
        Assert.Equal("1.0", catalog.SpecVersion);
        Assert.Equal(2, catalog.Entries.Count);

        var self = catalog.Entries[0];
        Assert.Equal("urn:air:localhost:mcp:agentregistry", self.Identifier);
        Assert.Equal("http://localhost/mcp/server-card", self.Url);
        Assert.Equal(ServerCardConstants.MediaType, self.Type);

        var entry = catalog.Entries[1];
        Assert.Equal($"urn:air:localhost:mcp:{Guid.Parse(live.Id):N}", entry.Identifier);
        Assert.Equal($"http://localhost/mcp/servers/{live.Id}", entry.Url);
        Assert.Equal(ServerCardConstants.MediaType, entry.Type);
        Assert.DoesNotContain(catalog.Entries, e => e.Url.EndsWith(dead.Id));
    }

    [Fact]
    public async Task AiCatalog_ExcludesAgentWhoseOnlyLiveEndpointIsNotMcp()
    {
        // Live A2A endpoint, dead MCP endpoint: not a live MCP server.
        var req = new RegisterAgentRequest("Mixed", null, null, null,
        [
            new EndpointRequest("a2a", TransportType.Http, ProtocolType.A2A,
                "https://mixed.example.com/a2a", LivenessModel.Ephemeral, 300, null, null),
            new EndpointRequest("mcp", TransportType.Http, ProtocolType.MCP,
                "https://mixed.example.com/mcp", LivenessModel.Ephemeral, 300, null, null),
        ]);
        var mixed = await PostAndDeserialize<AgentResponse>("/agents", req, _admin);
        factory.LivenessStore.Clear();
        var a2aEndpoint = mixed.Endpoints.Single(e => e.Protocol == "A2A");
        await _admin.PostAsync($"/agents/{mixed.Id}/endpoints/{a2aEndpoint.Id}/renew", null);

        var catalog = await _anon.GetFromJsonAsync<AiCatalog>("/.well-known/ai-catalog.json");

        Assert.DoesNotContain(catalog!.Entries, e => e.Url.EndsWith(mixed.Id));
        Assert.Single(catalog.Entries); // only the registry's own card
    }

    [Fact]
    public async Task AiCatalog_PagesThroughAllLiveServers()
    {
        for (var i = 0; i < 105; i++)
            await RegisterMcpServer($"Server {i:D3}");

        var catalog = await _anon.GetFromJsonAsync<AiCatalog>("/.well-known/ai-catalog.json");
        Assert.Equal(106, catalog!.Entries.Count);
    }

    [Fact]
    public async Task AiCatalog_EntryUrlsResolveToCards()
    {
        await RegisterMcpServer("Resolvable");
        var catalog = await _anon.GetFromJsonAsync<AiCatalog>("/.well-known/ai-catalog.json");

        foreach (var entry in catalog!.Entries)
        {
            var card = await _anon.GetFromJsonAsync<ServerCard>(new Uri(entry.Url).PathAndQuery);
            Assert.Equal(ServerCardConstants.SchemaUrl, card!.Schema);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void AssertCorsHeaders(HttpResponseMessage response)
    {
        Assert.Equal("*", Header(response, "Access-Control-Allow-Origin"));
        Assert.Equal("GET", Header(response, "Access-Control-Allow-Methods"));
        Assert.Equal("Content-Type, If-None-Match", Header(response, "Access-Control-Allow-Headers"));
        Assert.Equal("ETag", Header(response, "Access-Control-Expose-Headers"));
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(", ", values) : null;

    private static async Task AssertValidationError(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.True(problem!.RootElement.GetProperty("errors").TryGetProperty(field, out _),
            $"Expected a validation error for '{field}': {problem.RootElement}");
    }

    private async Task<AgentResponse> RegisterMcpServer(string name,
        string url = "https://mcp.example.com/mcp")
    {
        var req = new RegisterAgentRequest(name, null, null, null,
            [new EndpointRequest("streamable-http", TransportType.Http, ProtocolType.MCP,
                url, LivenessModel.Ephemeral, 300, null, null)]);
        return await PostAndDeserialize<AgentResponse>("/agents", req, _admin);
    }

    private async Task<ServerCard> RegisterCard(ServerCard card)
    {
        var response = await _agent.PostAsJsonAsync("/mcp/servers", card);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ServerCard>())!;
    }

    private static string IdOf(ServerCard card) => McpServerCardMapper.ReadRegistryMeta(card)!.Id!;

    private static ServerCard BuildSampleCard(string title) => new()
    {
        Schema = ServerCardConstants.SchemaUrl,
        Name = "com.example/weather",
        Version = "1.2.0",
        Title = title,
        Description = "Weather and web search.",
        WebsiteUrl = "https://example.com/weather",
        Repository = new ServerCardRepository { Url = "https://github.com/example/weather", Source = "github" },
        Remotes =
        [
            new ServerCardRemote
            {
                Type = "streamable-http",
                Url = "https://mcp.example.com/mcp",
                SupportedProtocolVersions = ["2026-07-28", "2025-11-25"],
                Headers =
                [
                    new ServerCardKeyValueInput
                    {
                        Name = "Authorization",
                        Description = "Bearer token",
                        IsRequired = true,
                        IsSecret = true,
                    },
                ],
            },
        ],
        Meta = new JsonObject
        {
            [ServerCardConstants.RegistryMetaKey] = JsonSerializer.SerializeToNode(new RegistryServerMeta
            {
                Instructions = "Use this server for weather and search tasks.",
                Tools =
                [
                    new McpToolDescriptor
                    {
                        Name = "get_weather",
                        Description = "Gets current weather",
                        InputSchema = new JsonObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JsonObject { ["location"] = new JsonObject { ["type"] = "string" } },
                        },
                    },
                    new McpToolDescriptor { Name = "search_web", Description = "Searches the web" },
                ],
                Resources =
                [
                    new McpResourceDescriptor { Uri = "file:///data/docs", Name = "docs", Description = "Documentation" },
                ],
            }, JsonSerializerOptions.Web),
        },
    };

    private static async Task<T> PostAndDeserialize<T>(string url, object body, HttpClient client)
    {
        var response = await client.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
