using AgentRegistry.Domain.Agents;

namespace AgentRegistry.Application.Agents;

/// <summary>
/// Orchestrates agent registration, liveness, and discovery use cases.
/// </summary>
public class AgentService(IAgentRepository repository, ILivenessStore livenessStore)
{
    // ── Registration ─────────────────────────────────────────────────────────

    public async Task<Agent> RegisterAsync(
        string name,
        string? description,
        string ownerId,
        IDictionary<string, string>? labels,
        IEnumerable<RegisterCapabilityRequest>? capabilities,
        IEnumerable<RegisterEndpointRequest>? endpoints,
        CancellationToken ct = default)
    {
        var agent = new Agent(AgentId.New(), name, description, ownerId, labels);

        if (capabilities is not null)
            foreach (var c in capabilities)
                agent.AddCapability(c.Name, c.Description, c.Tags);

        var addedEndpoints = new List<Endpoint>();
        if (endpoints is not null)
            foreach (var e in endpoints)
                addedEndpoints.Add(agent.AddEndpoint(
                    e.Name, e.Transport, e.Protocol, e.Address,
                    e.LivenessModel, e.TtlDuration, e.HeartbeatInterval, e.ProtocolMetadata));

        await repository.AddAsync(agent, ct);

        // Immediately publish liveness for all registered endpoints.
        foreach (var endpoint in addedEndpoints)
            await livenessStore.SetAliveAsync(endpoint.Id, endpoint.EffectiveLivenessTtl(), ct);

        return agent;
    }

    public async Task<Agent> UpdateAsync(
        AgentId id,
        string name,
        string? description,
        IDictionary<string, string>? labels,
        string requestingOwnerId,
        CancellationToken ct = default)
    {
        var agent = await GetOwnedAgentAsync(id, requestingOwnerId, ct);
        agent.Update(name, description, labels);
        await repository.UpdateAsync(agent, ct);
        return agent;
    }

    public async Task DeregisterAsync(AgentId id, string requestingOwnerId, CancellationToken ct = default)
    {
        var agent = await GetOwnedAgentAsync(id, requestingOwnerId, ct);
        var endpointIds = agent.Endpoints.Select(e => e.Id).ToList();
        await livenessStore.RemoveAllForAgentAsync(endpointIds, ct);
        await repository.DeleteAsync(id, ct);
    }

    // ── Endpoint management ───────────────────────────────────────────────────

    public async Task<Endpoint> AddEndpointAsync(
        AgentId agentId,
        RegisterEndpointRequest request,
        string requestingOwnerId,
        CancellationToken ct = default)
    {
        var agent = await GetOwnedAgentAsync(agentId, requestingOwnerId, ct);
        var endpoint = agent.AddEndpoint(
            request.Name, request.Transport, request.Protocol, request.Address,
            request.LivenessModel, request.TtlDuration, request.HeartbeatInterval, request.ProtocolMetadata);
        await repository.UpdateAsync(agent, ct);
        await livenessStore.SetAliveAsync(endpoint.Id, endpoint.EffectiveLivenessTtl(), ct);
        return endpoint;
    }

    public async Task RemoveEndpointAsync(AgentId agentId, EndpointId endpointId, string requestingOwnerId, CancellationToken ct = default)
    {
        var agent = await GetOwnedAgentAsync(agentId, requestingOwnerId, ct);
        if (!agent.RemoveEndpoint(endpointId))
            throw new NotFoundException($"Endpoint {endpointId} not found on agent {agentId}.");
        await repository.UpdateAsync(agent, ct);
        await livenessStore.RemoveAsync(endpointId, ct);
    }

    // ── Liveness ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Heartbeat for persistent endpoints — resets the liveness TTL.
    /// </summary>
    public async Task HeartbeatAsync(AgentId agentId, EndpointId endpointId, string requestingOwnerId, CancellationToken ct = default)
    {
        var agent = await GetOwnedAgentAsync(agentId, requestingOwnerId, ct);
        var endpoint = agent.FindEndpoint(endpointId)
            ?? throw new NotFoundException($"Endpoint {endpointId} not found on agent {agentId}.");

        if (endpoint.LivenessModel != LivenessModel.Persistent)
            throw new InvalidOperationException($"Endpoint {endpointId} uses {endpoint.LivenessModel} liveness; use /renew instead.");

        await livenessStore.SetAliveAsync(endpoint.Id, endpoint.EffectiveLivenessTtl(), ct);
    }

    /// <summary>
    /// TTL renewal for ephemeral endpoints.
    /// </summary>
    public async Task RenewAsync(AgentId agentId, EndpointId endpointId, string requestingOwnerId, CancellationToken ct = default)
    {
        var agent = await GetOwnedAgentAsync(agentId, requestingOwnerId, ct);
        var endpoint = agent.FindEndpoint(endpointId)
            ?? throw new NotFoundException($"Endpoint {endpointId} not found on agent {agentId}.");

        if (endpoint.LivenessModel != LivenessModel.Ephemeral)
            throw new InvalidOperationException($"Endpoint {endpointId} uses {endpoint.LivenessModel} liveness; use /heartbeat instead.");

        await livenessStore.SetAliveAsync(endpoint.Id, endpoint.EffectiveLivenessTtl(), ct);
    }

    // ── Discovery ─────────────────────────────────────────────────────────────

    public async Task<Agent?> GetByIdAsync(AgentId id, CancellationToken ct = default) =>
        await repository.FindByIdAsync(id, ct);

    public async Task<AgentWithLiveness?> GetByIdWithLivenessAsync(AgentId id, CancellationToken ct = default)
    {
        var agent = await repository.FindByIdAsync(id, ct);
        if (agent is null) return null;

        var liveEndpoints = await livenessStore.FilterAliveAsync(
            agent.Endpoints.Select(e => e.Id), ct);

        return new AgentWithLiveness(agent, liveEndpoints);
    }

    public async Task<PagedResult<AgentWithLiveness>> DiscoverAsync(AgentSearchFilter filter, CancellationToken ct = default)
    {
        var page = await repository.SearchAsync(filter, ct);

        // Gather all endpoint IDs from the result page and check liveness in one batch.
        var allEndpointIds = page.Items.SelectMany(a => a.Endpoints.Select(e => e.Id)).ToList();
        var liveSet = await livenessStore.FilterAliveAsync(allEndpointIds, ct);

        // Build per-agent live endpoint sets from the global batch result.
        var enriched = page.Items
            .Select(a => new AgentWithLiveness(
                a,
                a.Endpoints.Select(e => e.Id).Where(liveSet.Contains).ToHashSet()))
            .Where(a => !filter.LiveOnly || a.LiveEndpointIds.Count > 0)
            .ToList();

        return new PagedResult<AgentWithLiveness>(enriched, page.TotalCount, page.Page, page.PageSize);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<Agent> GetOwnedAgentAsync(AgentId id, string requestingOwnerId, CancellationToken ct)
    {
        var agent = await repository.FindByIdAsync(id, ct)
            ?? throw new NotFoundException($"Agent {id} not found.");

        if (agent.OwnerId != requestingOwnerId)
            throw new ForbiddenException($"Agent {id} belongs to a different owner.");

        return agent;
    }
}

// ── Supporting types ──────────────────────────────────────────────────────────

public record AgentWithLiveness(Agent Agent, IReadOnlySet<EndpointId> LiveEndpointIds);

public record RegisterCapabilityRequest(string Name, string? Description, IEnumerable<string>? Tags);

public record RegisterEndpointRequest(
    string Name,
    TransportType Transport,
    ProtocolType Protocol,
    string Address,
    LivenessModel LivenessModel,
    TimeSpan? TtlDuration,
    TimeSpan? HeartbeatInterval,
    string? ProtocolMetadata = null);
