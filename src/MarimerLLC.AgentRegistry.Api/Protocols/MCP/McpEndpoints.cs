using System.Security.Claims;
using MarimerLLC.AgentRegistry.Api.Auth;
using MarimerLLC.AgentRegistry.Api.Protocols.MCP.Models;
using MarimerLLC.AgentRegistry.Application.Agents;
using MarimerLLC.AgentRegistry.Domain.Agents;

namespace MarimerLLC.AgentRegistry.Api.Protocols.MCP;

public static class McpEndpoints
{
    /// <summary>MCP protocol revisions the registry's own MCP endpoint serves.</summary>
    public static readonly IReadOnlyList<string> SupportedProtocolVersions = ["2026-07-28", "2025-11-25"];

    private const int CatalogPageSize = 100;

    public static IEndpointRouteBuilder MapMcpEndpoints(this IEndpointRouteBuilder app)
    {
        // AI Catalog — domain-level discovery of every live MCP server card in the registry.
        app.MapGet("/.well-known/ai-catalog.json", GetAiCatalog)
            .WithName("McpAiCatalog")
            .WithTags("MCP")
            .WithSummary("AI Catalog of MCP Server Cards")
            .WithDescription(
                "Returns an AI Catalog (`application/ai-catalog+json`) listing the registry's own MCP Server Card " +
                "followed by one entry per live MCP server with a Streamable HTTP endpoint. Each entry links to a " +
                "SEP-2127 Server Card. Supports CORS, `Cache-Control` and ETag / `If-None-Match`.")
            .Produces<AiCatalog>(StatusCodes.Status200OK, ServerCardConstants.AiCatalogMediaType)
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        MapPreflight(app, "/.well-known/ai-catalog.json", "McpAiCatalogPreflight");

        var group = app.MapGroup("/mcp").WithTags("MCP");

        // The registry's own Server Card, at <streamable-http-url>/server-card.
        group.MapGet("/server-card", GetRegistryServerCard)
            .WithName("McpRegistryServerCard")
            .WithSummary("Registry's own MCP Server Card")
            .WithDescription(
                "Returns the SEP-2127 MCP Server Card describing the registry's own MCP server at `/mcp`. " +
                "Send `Accept: application/mcp-server-card+json` to get that media type back. " +
                "Supports CORS, `Cache-Control` and ETag / `If-None-Match`.")
            .Produces<ServerCard>(StatusCodes.Status200OK, ServerCardConstants.MediaType, "application/json")
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        MapPreflight(group, "/server-card", "McpRegistryServerCardPreflight");

        // Public discovery — no auth required.
        group.MapGet("/servers/{id}", GetServerCard)
            .WithName("McpGetServerCard")
            .WithSummary("Get an MCP Server Card")
            .WithDescription(
                "Returns the SEP-2127 MCP Server Card for a registered server: one `streamable-http` remote per MCP " +
                "HTTP endpoint, plus registry data (id, liveness, tool/resource/prompt descriptors) under " +
                "`_meta[\"llc.marimer/agentregistry\"]`. Send `Accept: application/mcp-server-card+json` to get that " +
                "media type back. Supports CORS, `Cache-Control` and ETag / `If-None-Match`. Returns 404 if the agent " +
                "has no MCP HTTP endpoints.")
            .Produces<ServerCard>(StatusCodes.Status200OK, ServerCardConstants.MediaType, "application/json")
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        MapPreflight(group, "/servers/{id}", "McpGetServerCardPreflight");

        group.MapGet("/servers", ListServerCards)
            .WithName("McpListServers")
            .WithSummary("List MCP Server Cards")
            .WithDescription(
                "Returns a paginated list of SEP-2127 MCP Server Cards. Only servers with a Streamable HTTP endpoint are included.\n\n" +
                "**Filters**\n" +
                "- `capability` — match servers that declare a tool with this exact name\n" +
                "- `tags` — comma-separated list; servers must match all supplied tags\n" +
                "- `liveOnly` — when `false`, includes servers with no live endpoints (default: `true`)\n" +
                "- `page` / `pageSize` — 1-based page number and page size (max 100, default 20)")
            .Produces<object>(StatusCodes.Status200OK);

        // MCP-native registration — submit a server card to register.
        group.MapPost("/servers", RegisterViaCard)
            .RequireAuthorization(RegistryPolicies.AgentOrAdmin)
            .WithName("McpRegisterServer")
            .WithSummary("Register an MCP server via Server Card")
            .WithDescription(
                "Registers an MCP server by submitting a SEP-2127 Server Card as the request body. The card must carry " +
                "the v1 `$schema`, a reverse-DNS `name`, a 1–100 character `description`, and at least one " +
                "`streamable-http` remote with an http(s) URL (`sse` remotes are ignored). Tool, resource and prompt " +
                "descriptors go in `_meta[\"llc.marimer/agentregistry\"]` and become searchable capabilities.")
            .Accepts<ServerCard>("application/json", ServerCardConstants.MediaType)
            .Produces<ServerCard>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    // ── Handlers ──────────────────────────────────────────────────────────────

    private static IResult GetRegistryServerCard(HttpRequest request) =>
        CardResults.Json(
            RegistrySelfCard(GetBaseUrl(request)),
            CardResults.NegotiateServerCardType(request),
            CardResults.SelfCardMaxAgeSeconds);

    private static async Task<IResult> GetServerCard(
        string id,
        AgentService agentService,
        HttpRequest request,
        CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid))
            return Results.Problem("Invalid server ID format.", statusCode: StatusCodes.Status400BadRequest);

        var result = await agentService.GetByIdWithLivenessAsync(new AgentId(guid), ct);
        if (result is null) return Results.NotFound();

        var card = McpServerCardMapper.ToServerCard(result);
        if (card is null)
            return Results.Problem($"Agent {id} has no MCP (HTTP) endpoints.", statusCode: StatusCodes.Status404NotFound);

        return CardResults.Json(card, CardResults.NegotiateServerCardType(request), CardResults.RegistryEntryMaxAgeSeconds);
    }

    private static async Task<IResult> GetAiCatalog(
        AgentService agentService,
        HttpRequest request,
        CancellationToken ct)
    {
        var baseUrl = GetBaseUrl(request);
        var host = request.Host.Host;

        var entries = new List<AiCatalogEntry>
        {
            new()
            {
                Identifier = $"urn:air:{host}:mcp:agentregistry",
                Type = ServerCardConstants.MediaType,
                Url = $"{baseUrl}/mcp/server-card",
            },
        };

        for (var page = 1; ; page++)
        {
            var result = await agentService.DiscoverAsync(new AgentSearchFilter(
                Protocol: ProtocolType.MCP,
                Transport: TransportType.Http,
                LiveOnly: true,
                Page: page,
                PageSize: CatalogPageSize), ct);

            // The search matches protocol, transport and liveness separately, on any endpoint.
            // Keep only agents whose MCP-over-HTTP endpoint is itself live.
            var liveServers = result.Items.Where(a => a.Agent.Endpoints.Any(e =>
                e.Protocol == ProtocolType.MCP &&
                e.Transport == TransportType.Http &&
                a.LiveEndpointIds.Contains(e.Id)));

            entries.AddRange(liveServers.Select(a => new AiCatalogEntry
            {
                Identifier = $"urn:air:{host}:mcp:{a.Agent.Id.Value:N}",
                Type = ServerCardConstants.MediaType,
                Url = $"{baseUrl}/mcp/servers/{a.Agent.Id}",
            }));

            if (!result.HasNextPage) break;
        }

        return CardResults.Json(
            new AiCatalog { Entries = entries },
            ServerCardConstants.AiCatalogMediaType,
            CardResults.RegistryEntryMaxAgeSeconds);
    }

    private static async Task<IResult> ListServerCards(
        AgentService agentService,
        string? capability = null,
        string? tags = null,
        bool liveOnly = true,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(1, page);

        var tagList = tags?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var filter = new AgentSearchFilter(
            CapabilityName: capability,
            Tags: tagList,
            Protocol: ProtocolType.MCP,
            Transport: TransportType.Http,
            LiveOnly: liveOnly,
            Page: page,
            PageSize: pageSize);

        var result = await agentService.DiscoverAsync(filter, ct);

        var cards = result.Items
            .Select(McpServerCardMapper.ToServerCard)
            .Where(c => c is not null)
            .ToList();

        return Results.Ok(new
        {
            items = cards,
            totalCount = result.TotalCount,
            page = result.Page,
            pageSize = result.PageSize,
            totalPages = result.TotalPages,
            hasNextPage = result.HasNextPage,
        });
    }

    private static async Task<IResult> RegisterViaCard(
        ServerCard card,
        AgentService agentService,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var mapped = McpServerCardMapper.FromServerCard(card);
        if (!mapped.IsValid)
            return Results.ValidationProblem(mapped.Errors!, title: "Invalid MCP Server Card (SEP-2127).");

        var registration = mapped.Registration!;
        var agent = await agentService.RegisterAsync(
            registration.Name,
            registration.Description,
            ownerId,
            labels: null,
            registration.Capabilities,
            registration.Endpoints,
            ct);

        var agentWithLiveness = await agentService.GetByIdWithLivenessAsync(agent.Id, ct);
        var created = McpServerCardMapper.ToServerCard(agentWithLiveness!);

        return Results.Created($"/mcp/servers/{agent.Id}", created);
    }

    // ── Registry self-card ────────────────────────────────────────────────────

    /// <summary>The registry's own MCP Server Card, served at <c>/mcp/server-card</c>.</summary>
    public static ServerCard RegistrySelfCard(string baseUrl) => new()
    {
        Schema = ServerCardConstants.SchemaUrl,
        Name = RegistryIdentity.Name,
        Version = RegistryIdentity.Version,
        Description = RegistryIdentity.Description,
        Title = RegistryIdentity.Title,
        WebsiteUrl = $"{baseUrl}/scalar/v1",
        Repository = new ServerCardRepository
        {
            Url = "https://github.com/MarimerLLC/agentregistry",
            Source = "github",
        },
        Remotes =
        [
            new ServerCardRemote
            {
                Type = ServerCardConstants.StreamableHttp,
                Url = $"{baseUrl}/mcp",
                SupportedProtocolVersions = SupportedProtocolVersions,
            },
        ],
    };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void MapPreflight(IEndpointRouteBuilder routes, string pattern, string name) =>
        routes.MapMethods(pattern, [HttpMethods.Options], CardResults.Preflight)
            .WithName(name)
            .WithTags("MCP")
            .WithSummary("CORS preflight")
            .WithDescription("Answers browser CORS preflight requests for this public discovery document.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

    private static string GetBaseUrl(HttpRequest request) =>
        $"{request.Scheme}://{request.Host}{request.PathBase}";
}
