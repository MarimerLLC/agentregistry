using System.Security.Claims;
using AgentRegistry.Api.Auth;
using AgentRegistry.Api.Protocols.ACP.Models;
using AgentRegistry.Application.Agents;
using AgentRegistry.Domain.Agents;

namespace AgentRegistry.Api.Protocols.ACP;

public static class AcpEndpoints
{
    public static IEndpointRouteBuilder MapAcpEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/acp").WithTags("ACP");

        // Public discovery — mirrors the ACP /agents convention in the registry namespace.
        group.MapGet("/agents/{id}", GetManifest).WithName("AcpGetAgentManifest");
        group.MapGet("/agents", ListManifests).WithName("AcpListAgents");

        // ACP-native registration — submit a manifest to register.
        group.MapPost("/agents", RegisterViaManifest)
            .RequireAuthorization(RegistryPolicies.AgentOrAdmin)
            .WithName("AcpRegisterAgent");

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
