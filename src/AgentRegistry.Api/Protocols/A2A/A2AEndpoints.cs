using System.Security.Claims;
using MarimerLLC.AgentRegistry.Api.Auth;
using MarimerLLC.AgentRegistry.Api.Protocols.A2A.Models;
using MarimerLLC.AgentRegistry.Application.Agents;
using MarimerLLC.AgentRegistry.Domain.Agents;

namespace MarimerLLC.AgentRegistry.Api.Protocols.A2A;

public static class A2AEndpoints
{
    public static IEndpointRouteBuilder MapA2AEndpoints(this IEndpointRouteBuilder app)
    {
        // The registry's own agent card — describes the registry as an A2A agent.
        app.MapGet("/.well-known/agent.json", GetRegistryCard)
            .WithName("A2ARegistryCard")
            .WithTags("A2A")
            .WithSummary("Registry's own A2A agent card")
            .WithDescription("Returns the A2A agent card that describes the registry itself as an A2A-capable agent, following the A2A v1.0 RC well-known URL convention.")
            .Produces<object>(StatusCodes.Status200OK);

        var group = app.MapGroup("/a2a").WithTags("A2A");

        // Per-agent card — public, no auth required.
        group.MapGet("/agents/{id}", GetAgentCard)
            .WithName("A2AGetAgentCard")
            .WithSummary("Get an A2A agent card")
            .WithDescription("Returns the A2A-spec agent card for a registered agent. Skills are mapped from the agent's capabilities. Returns 404 if the agent does not exist or has no A2A endpoints.")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // A2A-native registration — submit an agent card to register.
        group.MapPost("/agents", RegisterViaCard)
            .RequireAuthorization(RegistryPolicies.AgentOrAdmin)
            .WithName("A2ARegisterAgent")
            .WithSummary("Register an agent via A2A agent card")
            .WithDescription("Registers an agent by submitting a native A2A agent card. Capabilities are mapped from the card's skills and endpoints from the card's service endpoints. Returns the stored card.")
            .Produces<object>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static IResult GetRegistryCard(HttpRequest request)
    {
        var baseUrl = GetBaseUrl(request);
        var card = A2AAgentCardMapper.RegistrySelfCard(baseUrl);
        return Results.Ok(card);
    }

    private static async Task<IResult> GetAgentCard(
        string id,
        AgentService agentService,
        HttpRequest request,
        CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid))
            return Results.BadRequest("Invalid agent ID format.");

        var result = await agentService.GetByIdWithLivenessAsync(new AgentId(guid), ct);
        if (result is null) return Results.NotFound();

        var card = A2AAgentCardMapper.ToAgentCard(result, GetBaseUrl(request));
        if (card is null)
            return Results.NotFound(new { error = $"Agent {id} has no A2A endpoints." });

        return Results.Ok(card);
    }

    private static async Task<IResult> RegisterViaCard(
        RegisterViaCardRequest request,
        AgentService agentService,
        ClaimsPrincipal user,
        HttpRequest httpRequest,
        CancellationToken ct)
    {
        var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var mapped = A2AAgentCardMapper.FromAgentCard(request.Card);

        var agent = await agentService.RegisterAsync(
            mapped.Name,
            mapped.Description,
            ownerId,
            labels: null,
            mapped.Capabilities,
            mapped.Endpoints,
            ct);

        var agentWithLiveness = await agentService.GetByIdWithLivenessAsync(agent.Id, ct);
        var card = A2AAgentCardMapper.ToAgentCard(agentWithLiveness!, GetBaseUrl(httpRequest));

        return Results.Created($"/a2a/agents/{agent.Id}", card);
    }

    private static string GetBaseUrl(HttpRequest request) =>
        $"{request.Scheme}://{request.Host}";
}
