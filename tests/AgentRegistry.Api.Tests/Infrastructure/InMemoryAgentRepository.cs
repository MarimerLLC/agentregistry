using System.Collections.Concurrent;
using MarimerLLC.AgentRegistry.Application.Agents;
using MarimerLLC.AgentRegistry.Domain.Agents;

namespace MarimerLLC.AgentRegistry.Api.Tests.Infrastructure;

public class InMemoryAgentRepository : IAgentRepository
{
    private readonly ConcurrentDictionary<AgentId, Agent> _store = new();

    public Task<Agent?> FindByIdAsync(AgentId id, CancellationToken ct = default) =>
        Task.FromResult(_store.GetValueOrDefault(id));

    public Task<PagedResult<Agent>> SearchAsync(AgentSearchFilter filter, CancellationToken ct = default)
    {
        var query = _store.Values.AsEnumerable();

        if (filter.OwnerId is not null)
            query = query.Where(a => a.OwnerId == filter.OwnerId);

        if (filter.CapabilityName is not null)
            query = query.Where(a => a.Capabilities.Any(c =>
                c.Name.Contains(filter.CapabilityName, StringComparison.OrdinalIgnoreCase)));

        if (filter.Protocol.HasValue)
            query = query.Where(a => a.Endpoints.Any(e => e.Protocol == filter.Protocol.Value));

        if (filter.Transport.HasValue)
            query = query.Where(a => a.Endpoints.Any(e => e.Transport == filter.Transport.Value));

        if (filter.Tags is { Count: > 0 })
            query = query.Where(a => a.Capabilities.Any(c =>
                filter.Tags.All(t => c.Tags.Contains(t))));

        var all = query.OrderBy(a => a.Name).ToList();
        var paged = all.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToList();

        return Task.FromResult(new PagedResult<Agent>(paged, all.Count, filter.Page, filter.PageSize));
    }

    public Task AddAsync(Agent agent, CancellationToken ct = default)
    {
        _store[agent.Id] = agent;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Agent agent, CancellationToken ct = default)
    {
        _store[agent.Id] = agent;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(AgentId id, CancellationToken ct = default)
    {
        var removed = _store.TryRemove(id, out _);
        return Task.FromResult(removed);
    }

    public Task<IReadOnlyList<Endpoint>> GetEphemeralEndpointsAliveAfterAsync(
        DateTimeOffset since, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Endpoint>>(
            _store.Values
                .SelectMany(a => a.Endpoints)
                .Where(e => e.LivenessModel == LivenessModel.Ephemeral && e.LastAliveAt >= since)
                .ToList());

    public void Clear() => _store.Clear();
}
