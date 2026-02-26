using System.Security.Claims;
using AgentRegistry.Api.Auth;
using AgentRegistry.Api.Protocols.A2A.Models;
using AgentRegistry.Application.Agents;
using AgentRegistry.Domain.Agents;

namespace AgentRegistry.Api.Protocols.A2A;

public static class A2AEndpoints
{
    public static IEndpointRouteBuilder MapA2AEndpoints(this IEndpointRouteBuilder app)
    {
        // The registry's own agent card — describes the registry as an A2A agent.
        app.MapGet("/.well-known/agent.json", GetRegistryCard)
            .WithName("A2ARegistryCard")
            .WithTags("A2A");

        var group = app.MapGroup("/a2a").WithTags("A2A");

        // Per-agent card — public, no auth required.
        group.MapGet("/agents/{id}", GetAgentCard)
            .WithName("A2AGetAgentCard");

        // A2A-native registration — submit an agent card to register.
        group.MapPost("/agents", RegisterViaCard)
            .RequireAuthorization(RegistryPolicies.AgentOrAdmin)
            .WithName("A2ARegisterAgent");

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
