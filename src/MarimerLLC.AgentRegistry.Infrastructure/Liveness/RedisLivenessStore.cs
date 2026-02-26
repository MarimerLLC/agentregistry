using MarimerLLC.AgentRegistry.Application.Agents;
using MarimerLLC.AgentRegistry.Domain.Agents;
using StackExchange.Redis;

namespace MarimerLLC.AgentRegistry.Infrastructure.Liveness;

public class RedisLivenessStore(IConnectionMultiplexer redis) : ILivenessStore
{
    private readonly IDatabase _db = redis.GetDatabase();

    private static RedisKey KeyFor(EndpointId id) => $"endpoint:liveness:{id}";

    public Task SetAliveAsync(EndpointId endpointId, TimeSpan ttl, CancellationToken ct = default) =>
        _db.StringSetAsync(KeyFor(endpointId), "1", ttl);

    public async Task<bool> IsAliveAsync(EndpointId endpointId, CancellationToken ct = default) =>
        await _db.KeyExistsAsync(KeyFor(endpointId));

    public async Task<IReadOnlySet<EndpointId>> FilterAliveAsync(
        IEnumerable<EndpointId> endpointIds, CancellationToken ct = default)
    {
        var ids = endpointIds.ToList();
        if (ids.Count == 0) return new HashSet<EndpointId>();

        var keys = ids.Select(id => KeyFor(id)).ToArray();
        var results = await _db.KeyExistsAsync(keys);

        // KeyExistsAsync with multiple keys returns the count of existing keys,
        // not a per-key result. We need per-key checks.
        // Use a batch of StringGetAsync instead.
        var batch = _db.CreateBatch();
        var tasks = keys.Select(k => batch.StringGetAsync(k)).ToArray();
        batch.Execute();
        await Task.WhenAll(tasks);

        var alive = new HashSet<EndpointId>();
        for (var i = 0; i < ids.Count; i++)
            if (tasks[i].Result.HasValue)
                alive.Add(ids[i]);

        return alive;
    }

    public Task RemoveAsync(EndpointId endpointId, CancellationToken ct = default) =>
        _db.KeyDeleteAsync(KeyFor(endpointId));

    public async Task RemoveAllForAgentAsync(IEnumerable<EndpointId> endpointIds, CancellationToken ct = default)
    {
        var keys = endpointIds.Select(id => KeyFor(id)).ToArray();
        if (keys.Length > 0)
            await _db.KeyDeleteAsync(keys);
    }
}
