using System.Security.Claims;
using MarimerLLC.AgentRegistry.Api.Auth;
using MarimerLLC.AgentRegistry.Api.Protocols.ACP.Models;
using MarimerLLC.AgentRegistry.Application.Agents;
using MarimerLLC.AgentRegistry.Domain.Agents;

namespace MarimerLLC.AgentRegistry.Api.Protocols.ACP;

public static class AcpEndpoints
{
    public static IEndpointRouteBuilder MapAcpEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/acp").WithTags("ACP");

        // Public discovery — mirrors the ACP /agents convention in the registry namespace.
        group.MapGet("/agents/{id}", GetManifest)
            .WithName("AcpGetAgentManifest")
            .WithSummary("Get an ACP agent manifest")
            .WithDescription("Returns the ACP 0.2.0 agent manifest for a registered agent, including MIME-typed content types, JSON Schema for inputs and outputs, and performance metrics. Returns 404 if the agent has no ACP endpoints.")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/agents", ListManifests)
            .WithName("AcpListAgents")
            .WithSummary("List ACP agent manifests")
            .WithDescription(
                "Returns a paginated list of ACP agent manifests for registered agents.\n\n" +
                "**Filters**\n" +
                "- `capability` — match agents that declare a capability with this exact name\n" +
                "- `tags` — comma-separated list; agents must match all supplied tags\n" +
                "- `domain` — ACP domain filter (stored as a tag, merged with `tags`)\n" +
                "- `liveOnly` — when `false`, includes agents with no live endpoints (default: `true`)\n" +
                "- `page` / `pageSize` — 1-based page number and page size (max 100, default 20)")
            .Produces<object>(StatusCodes.Status200OK);

        // ACP-native registration — submit a manifest to register.
        group.MapPost("/agents", RegisterViaManifest)
            .RequireAuthorization(RegistryPolicies.AgentOrAdmin)
            .WithName("AcpRegisterAgent")
            .WithSummary("Register an agent via ACP manifest")
            .WithDescription("Registers an agent by submitting a native ACP 0.2.0 manifest and the agent's endpoint URL. Capabilities are mapped from the manifest's metadata and all manifest fields round-trip through protocol metadata.")
            .Produces<object>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> GetManifest(
        string id,
        AgentService agentService,
        CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid))
            return Results.BadRequest("Invalid agent ID format.");

        var result = await agentService.GetByIdWithLivenessAsync(new AgentId(guid), ct);
        if (result is null) return Results.NotFound();

        var manifest = AcpAgentManifestMapper.ToManifest(result);
        if (manifest is null)
            return Results.NotFound(new { error = $"Agent {id} has no ACP endpoints." });

        return Results.Ok(manifest);
    }

    private static async Task<IResult> ListManifests(
        AgentService agentService,
        string? capability = null,
        string? tags = null,
        string? domain = null,
        bool liveOnly = true,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(1, page);

        // Merge domain into the tag filter if supplied (ACP domains are stored as tags).
        var tagList = tags?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList() ?? [];
        if (!string.IsNullOrWhiteSpace(domain))
            tagList.Add(domain);

        var filter = new AgentSearchFilter(
            CapabilityName: capability,
            Tags: tagList.Count > 0 ? tagList : null,
            Protocol: ProtocolType.ACP,
            Transport: TransportType.Http,
            LiveOnly: liveOnly,
            Page: page,
            PageSize: pageSize);

        var result = await agentService.DiscoverAsync(filter, ct);

        var manifests = result.Items
            .Select(AcpAgentManifestMapper.ToManifest)
            .Where(m => m is not null)
            .ToList();

        return Results.Ok(new
        {
            agents = manifests,
            totalCount = result.TotalCount,
            page = result.Page,
            pageSize = result.PageSize,
            totalPages = result.TotalPages,
            hasNextPage = result.HasNextPage,
        });
    }

    private static async Task<IResult> RegisterViaManifest(
        RegisterViaAcpRequest request,
        AgentService agentService,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.EndpointUrl))
            return Results.BadRequest("endpoint_url is required — the base URL of the agent's ACP server.");

        var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var mapped = AcpAgentManifestMapper.FromManifest(request.Manifest, request.EndpointUrl);

        var agent = await agentService.RegisterAsync(
            mapped.Name,
            mapped.Description,
            ownerId,
            labels: null,
            mapped.Capabilities,
            mapped.Endpoints,
            ct);

        var agentWithLiveness = await agentService.GetByIdWithLivenessAsync(agent.Id, ct);
        var manifest = AcpAgentManifestMapper.ToManifest(agentWithLiveness!);

        return Results.Created($"/acp/agents/{agent.Id}", manifest);
    }
}
