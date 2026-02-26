using MarimerLLC.AgentRegistry.Api.Agents.Models;
using MarimerLLC.AgentRegistry.Application.Agents;
using MarimerLLC.AgentRegistry.Domain.Agents;

namespace MarimerLLC.AgentRegistry.Api.Agents;

public static class DiscoveryEndpoints
{
    public static IEndpointRouteBuilder MapDiscoveryEndpoints(this IEndpointRouteBuilder app)
    {
        // Discovery is intentionally unauthenticated — the registry is a public index.
        // Individual agents may enforce their own auth on their endpoints.
        var group = app.MapGroup("/discover");

        group.MapGet("/agents", DiscoverAgents)
            .WithName("DiscoverAgents")
            .WithTags("Agents")
            .WithSummary("Discover registered agents")
            .WithDescription(
                "Returns a paginated list of agents matching the given filters. By default only agents " +
                "with at least one live endpoint in Redis are returned.\n\n" +
                "**Filters**\n" +
                "- `capability` — match agents that declare a capability with this exact name\n" +
                "- `tags` — comma-separated list; agents must match all supplied tags\n" +
                "- `protocol` — filter by protocol: `A2A`, `MCP`, `ACP`, or `Generic`\n" +
                "- `transport` — filter by transport: `Http`, `AzureServiceBus`, or `Amqp`\n" +
                "- `liveOnly` — when `false`, includes agents with no live endpoints (default: `true`)\n" +
                "- `page` / `pageSize` — 1-based page number and page size (max 100, default 20)")
            .Produces<PagedAgentResponse>(StatusCodes.Status200OK);

        return app;
    }

    private static async Task<IResult> DiscoverAgents(
        AgentService agentService,
        string? capability = null,
        string? tags = null,
        string? protocol = null,
        string? transport = null,
        bool liveOnly = true,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        ProtocolType? protocolType = protocol is not null && Enum.TryParse<ProtocolType>(protocol, ignoreCase: true, out var p) ? p : null;
        TransportType? transportType = transport is not null && Enum.TryParse<TransportType>(transport, ignoreCase: true, out var t) ? t : null;
        var tagList = tags?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(1, page);

        var filter = new AgentSearchFilter(
            CapabilityName: capability,
            Tags: tagList,
            Protocol: protocolType,
            Transport: transportType,
            LiveOnly: liveOnly,
            Page: page,
            PageSize: pageSize);

        var result = await agentService.DiscoverAsync(filter, ct);

        return Results.Ok(new PagedAgentResponse(
            result.Items.Select(AgentResponse.From).ToList(),
            result.TotalCount,
            result.Page,
            result.PageSize,
            result.TotalPages,
            result.HasNextPage,
            result.HasPreviousPage));
    }
}
