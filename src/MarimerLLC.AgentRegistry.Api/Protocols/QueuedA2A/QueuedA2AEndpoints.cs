using System.Security.Claims;
using MarimerLLC.AgentRegistry.Api.Auth;
using MarimerLLC.AgentRegistry.Api.Protocols.QueuedA2A.Models;
using MarimerLLC.AgentRegistry.Application.Agents;
using MarimerLLC.AgentRegistry.Domain.Agents;

namespace MarimerLLC.AgentRegistry.Api.Protocols.QueuedA2A;

public static class QueuedA2AEndpoints
{
    public static IEndpointRouteBuilder MapQueuedA2AEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/a2a/async").WithTags("QueuedA2A");

        // Public discovery
        group.MapGet("/agents/{id}", GetCard)
            .WithName("QueuedA2AGetAgentCard")
            .WithSummary("Get a queued A2A agent card")
            .WithDescription(
                "Returns the A2A agent card for an agent registered with a message-queue endpoint. " +
                "The card includes the broker connection details (technology, host, exchange, topic) " +
                "needed for a client to publish A2A task messages directly to the agent. " +
                "Returns 404 if the agent exists but has no queued A2A endpoints.")
            .Produces<QueuedAgentCard>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/agents", ListCards)
            .WithName("QueuedA2AListAgents")
            .WithSummary("List queued A2A agent cards")
            .WithDescription(
                "Returns a paginated list of A2A agent cards for agents with message-queue endpoints.\n\n" +
                "**Filters**\n" +
                "- `capability` — match agents that declare a capability with this exact name\n" +
                "- `tags` — comma-separated list; agents must match all supplied tags\n" +
                "- `liveOnly` — when `false`, includes agents with no live endpoints (default: `true`)\n" +
                "- `page` / `pageSize` — 1-based page number and page size (max 100, default 20)")
            .Produces<object>(StatusCodes.Status200OK);

        // Authenticated registration
        group.MapPost("/agents", RegisterViaCard)
            .RequireAuthorization(RegistryPolicies.AgentOrAdmin)
            .WithName("QueuedA2ARegisterAgent")
            .WithSummary("Register a queued A2A agent")
            .WithDescription(
                "Registers an agent that communicates via an async message broker using the A2A protocol. " +
                "Supply the A2A agent card along with the queue endpoint connection details. " +
                "All card fields round-trip through protocol metadata for faithful reconstruction on discovery.")
            .Produces<QueuedAgentCard>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> GetCard(
        string id,
        AgentService agentService,
        CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid))
            return Results.BadRequest("Invalid agent ID format.");

        var result = await agentService.GetByIdWithLivenessAsync(new AgentId(guid), ct);
        if (result is null) return Results.NotFound();

        var card = QueuedA2AMapper.ToCard(result);
        if (card is null)
            return Results.NotFound(new { error = $"Agent {id} has no queued A2A endpoints." });

        return Results.Ok(card);
    }

    private static async Task<IResult> ListCards(
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

        // Filter by A2A protocol; transport filter is omitted so both Amqp and AzureServiceBus
        // are returned — the mapper filters out Http endpoints when building cards.
        var filter = new AgentSearchFilter(
            CapabilityName: capability,
            Tags: tagList?.Count > 0 ? tagList : null,
            Protocol: ProtocolType.A2A,
            Transport: null,
            LiveOnly: liveOnly,
            Page: page,
            PageSize: pageSize);

        var result = await agentService.DiscoverAsync(filter, ct);

        var cards = result.Items
            .Select(QueuedA2AMapper.ToCard)
            .Where(c => c is not null)
            .ToList();

        return Results.Ok(new
        {
            agents = cards,
            totalCount = result.TotalCount,
            page = result.Page,
            pageSize = result.PageSize,
            totalPages = result.TotalPages,
            hasNextPage = result.HasNextPage,
        });
    }

    private static async Task<IResult> RegisterViaCard(
        QueuedAgentCard card,
        AgentService agentService,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(card.QueueEndpoint?.TaskTopic))
            return Results.BadRequest("queueEndpoint.taskTopic is required — the routing key or topic path clients publish task messages to.");

        if (string.IsNullOrWhiteSpace(card.QueueEndpoint?.Technology))
            return Results.BadRequest("queueEndpoint.technology is required (e.g. \"rabbitmq\" or \"azure-service-bus\").");

        var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var mapped = QueuedA2AMapper.FromCard(card);

        var agent = await agentService.RegisterAsync(
            mapped.Name,
            mapped.Description,
            ownerId,
            labels: null,
            mapped.Capabilities,
            mapped.Endpoints,
            ct);

        var agentWithLiveness = await agentService.GetByIdWithLivenessAsync(agent.Id, ct);
        var registered = QueuedA2AMapper.ToCard(agentWithLiveness!);

        return Results.Created($"/a2a/async/agents/{agent.Id}", registered);
    }
}
