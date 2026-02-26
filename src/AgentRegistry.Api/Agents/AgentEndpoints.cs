using System.Security.Claims;
using AgentRegistry.Api.Agents.Models;
using AgentRegistry.Api.Auth;
using AgentRegistry.Application;
using AgentRegistry.Application.Agents;
using AgentRegistry.Domain.Agents;

namespace AgentRegistry.Api.Agents;

public static class AgentEndpoints
{
    public static IEndpointRouteBuilder MapAgentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/agents").RequireAuthorization(RegistryPolicies.AgentOrAdmin);

        group.MapPost("/", RegisterAgent).WithName("RegisterAgent");
        group.MapGet("/{id}", GetAgent).WithName("GetAgent");
        group.MapPut("/{id}", UpdateAgent).WithName("UpdateAgent");
        group.MapDelete("/{id}", DeregisterAgent).WithName("DeregisterAgent");

        // Endpoint management
        group.MapPost("/{agentId}/endpoints", AddEndpoint).WithName("AddEndpoint");
        group.MapDelete("/{agentId}/endpoints/{endpointId}", RemoveEndpoint).WithName("RemoveEndpoint");

        // Liveness
        group.MapPost("/{agentId}/endpoints/{endpointId}/heartbeat", Heartbeat).WithName("Heartbeat");
        group.MapPost("/{agentId}/endpoints/{endpointId}/renew", Renew).WithName("RenewEndpoint");

        return app;
    }

    private static async Task<IResult> RegisterAgent(
        RegisterAgentRequest request,
        AgentService agentService,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var agent = await agentService.RegisterAsync(
            request.Name,
            request.Description,
            ownerId,
            request.Labels,
            request.Capabilities?.Select(c => new RegisterCapabilityRequest(c.Name, c.Description, c.Tags)),
            request.Endpoints?.Select(e => new RegisterEndpointRequest(
                e.Name, e.Transport, e.Protocol, e.Address, e.LivenessModel,
                e.TtlSeconds.HasValue ? TimeSpan.FromSeconds(e.TtlSeconds.Value) : null,
                e.HeartbeatIntervalSeconds.HasValue ? TimeSpan.FromSeconds(e.HeartbeatIntervalSeconds.Value) : null,
                e.ProtocolMetadata)),
            ct);

        return Results.Created($"/agents/{agent.Id}", AgentResponse.From(agent));
    }

    private static async Task<IResult> GetAgent(
        string id,
        AgentService agentService,
        CancellationToken ct)
    {
        if (!TryParseAgentId(id, out var agentId))
            return Results.BadRequest("Invalid agent ID format.");

        var result = await agentService.GetByIdWithLivenessAsync(agentId, ct);
        return result is null ? Results.NotFound() : Results.Ok(AgentResponse.From(result));
    }

    private static async Task<IResult> UpdateAgent(
        string id,
        UpdateAgentRequest request,
        AgentService agentService,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (!TryParseAgentId(id, out var agentId))
            return Results.BadRequest("Invalid agent ID format.");

        var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;

        try
        {
            var agent = await agentService.UpdateAsync(agentId, request.Name, request.Description, request.Labels, ownerId, ct);
            return Results.Ok(AgentResponse.From(agent));
        }
        catch (NotFoundException) { return Results.NotFound(); }
        catch (ForbiddenException) { return Results.Forbid(); }
    }

    private static async Task<IResult> DeregisterAgent(
        string id,
        AgentService agentService,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (!TryParseAgentId(id, out var agentId))
            return Results.BadRequest("Invalid agent ID format.");

        var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;

        try
        {
            await agentService.DeregisterAsync(agentId, ownerId, ct);
            return Results.NoContent();
        }
        catch (NotFoundException) { return Results.NotFound(); }
        catch (ForbiddenException) { return Results.Forbid(); }
    }

    private static async Task<IResult> AddEndpoint(
        string agentId,
        EndpointRequest request,
        AgentService agentService,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (!TryParseAgentId(agentId, out var aid))
            return Results.BadRequest("Invalid agent ID format.");

        var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;

        try
        {
            var endpoint = await agentService.AddEndpointAsync(
                aid,
                new RegisterEndpointRequest(
                    request.Name, request.Transport, request.Protocol, request.Address, request.LivenessModel,
                    request.TtlSeconds.HasValue ? TimeSpan.FromSeconds(request.TtlSeconds.Value) : null,
                    request.HeartbeatIntervalSeconds.HasValue ? TimeSpan.FromSeconds(request.HeartbeatIntervalSeconds.Value) : null,
                    request.ProtocolMetadata),
                ownerId,
                ct);
            return Results.Created($"/agents/{agentId}/endpoints/{endpoint.Id}", EndpointResponse.From(endpoint, isLive: true));
        }
        catch (NotFoundException) { return Results.NotFound(); }
        catch (ForbiddenException) { return Results.Forbid(); }
    }

    private static async Task<IResult> RemoveEndpoint(
        string agentId,
        string endpointId,
        AgentService agentService,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (!TryParseAgentId(agentId, out var aid)) return Results.BadRequest("Invalid agent ID.");
        if (!TryParseEndpointId(endpointId, out var eid)) return Results.BadRequest("Invalid endpoint ID.");

        var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;

        try
        {
            await agentService.RemoveEndpointAsync(aid, eid, ownerId, ct);
            return Results.NoContent();
        }
        catch (NotFoundException) { return Results.NotFound(); }
        catch (ForbiddenException) { return Results.Forbid(); }
    }

    private static async Task<IResult> Heartbeat(
        string agentId,
        string endpointId,
        AgentService agentService,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (!TryParseAgentId(agentId, out var aid)) return Results.BadRequest("Invalid agent ID.");
        if (!TryParseEndpointId(endpointId, out var eid)) return Results.BadRequest("Invalid endpoint ID.");

        var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;

        try
        {
            await agentService.HeartbeatAsync(aid, eid, ownerId, ct);
            return Results.NoContent();
        }
        catch (NotFoundException) { return Results.NotFound(); }
        catch (ForbiddenException) { return Results.Forbid(); }
        catch (InvalidOperationException ex) { return Results.BadRequest(ex.Message); }
    }

    private static async Task<IResult> Renew(
        string agentId,
        string endpointId,
        AgentService agentService,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (!TryParseAgentId(agentId, out var aid)) return Results.BadRequest("Invalid agent ID.");
        if (!TryParseEndpointId(endpointId, out var eid)) return Results.BadRequest("Invalid endpoint ID.");

        var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;

        try
        {
            await agentService.RenewAsync(aid, eid, ownerId, ct);
            return Results.NoContent();
        }
        catch (NotFoundException) { return Results.NotFound(); }
        catch (ForbiddenException) { return Results.Forbid(); }
        catch (InvalidOperationException ex) { return Results.BadRequest(ex.Message); }
    }

    private static bool TryParseAgentId(string raw, out AgentId id)
    {
        if (Guid.TryParse(raw, out var g)) { id = new AgentId(g); return true; }
        id = default; return false;
    }

    private static bool TryParseEndpointId(string raw, out EndpointId id)
    {
        if (Guid.TryParse(raw, out var g)) { id = new EndpointId(g); return true; }
        id = default; return false;
    }
}
