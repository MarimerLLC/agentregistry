using System.Collections.Concurrent;
using MarimerLLC.AgentRegistry.Application.Agents;
using MarimerLLC.AgentRegistry.Domain.Agents;

namespace MarimerLLC.AgentRegistry.Api.Tests.Infrastructure;

public class InMemoryLivenessStore : ILivenessStore
{
    private readonly ConcurrentDictionary<EndpointId, DateTimeOffset> _ttls = new();

    public Task SetAliveAsync(EndpointId endpointId, TimeSpan ttl, CancellationToken ct = default)
    {
        _ttls[endpointId] = DateTimeOffset.UtcNow.Add(ttl);
        return Task.CompletedTask;
    }

    public Task<bool> IsAliveAsync(EndpointId endpointId, CancellationToken ct = default) =>
        Task.FromResult(_ttls.TryGetValue(endpointId, out var exp) && exp > DateTimeOffset.UtcNow);

    public Task<IReadOnlySet<EndpointId>> FilterAliveAsync(IEnumerable<EndpointId> endpointIds, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var alive = endpointIds
            .Where(id => _ttls.TryGetValue(id, out var exp) && exp > now)
            .ToHashSet();
        return Task.FromResult<IReadOnlySet<EndpointId>>(alive);
    }

    public Task RemoveAsync(EndpointId endpointId, CancellationToken ct = default)
    {
        _ttls.TryRemove(endpointId, out _);
        return Task.CompletedTask;
    }

    public Task RemoveAllForAgentAsync(IEnumerable<EndpointId> endpointIds, CancellationToken ct = default)
    {
        foreach (var id in endpointIds) _ttls.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    public void Clear() => _ttls.Clear();
}
