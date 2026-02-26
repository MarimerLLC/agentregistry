using System.Security.Claims;
using AgentRegistry.Api.Auth;
using AgentRegistry.Api.Protocols.MCP.Models;
using AgentRegistry.Application.Agents;
using AgentRegistry.Domain.Agents;

namespace AgentRegistry.Api.Protocols.MCP;

public static class McpEndpoints
{
    public static IEndpointRouteBuilder MapMcpEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/mcp").WithTags("MCP");

        // Public discovery — no auth required.
        group.MapGet("/servers/{id}", GetServerCard).WithName("McpGetServerCard");
        group.MapGet("/servers", ListServerCards).WithName("McpListServers");

        // MCP-native registration — submit a server card to register.
        group.MapPost("/servers", RegisterViaCard)
            .RequireAuthorization(RegistryPolicies.AgentOrAdmin)
            .WithName("McpRegisterServer");

        return app;
    }

    private static async Task<IResult> GetServerCard(
        string id,
        AgentService agentService,
        CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid))
            return Results.BadRequest("Invalid server ID format.");

        var result = await agentService.GetByIdWithLivenessAsync(new AgentId(guid), ct);
        if (result is null) return Results.NotFound();

        var card = McpServerCardMapper.ToServerCard(result);
        if (card is null)
            return Results.NotFound(new { error = $"Agent {id} has no MCP (HTTP) endpoints." });

        return Results.Ok(card);
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
        RegisterViaMcpRequest request,
        AgentService agentService,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var mapped = McpServerCardMapper.FromServerCard(request.ServerCard);

        if (!mapped.Endpoints.Any())
            return Results.BadRequest("Server card must include at least one HTTP endpoint in endpoints.streamableHttp.");

        var agent = await agentService.RegisterAsync(
            mapped.Name,
            mapped.Description,
            ownerId,
            labels: null,
            mapped.Capabilities,
            mapped.Endpoints,
            ct);

        var agentWithLiveness = await agentService.GetByIdWithLivenessAsync(agent.Id, ct);
        var card = McpServerCardMapper.ToServerCard(agentWithLiveness!);

        return Results.Created($"/mcp/servers/{agent.Id}", card);
    }
}
