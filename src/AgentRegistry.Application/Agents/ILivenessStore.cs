using MarimerLLC.AgentRegistry.Domain.Agents;

namespace MarimerLLC.AgentRegistry.Application.Agents;

/// <summary>
/// Fast TTL-based store for endpoint liveness state.
/// Backed by Redis in production; in-memory for tests.
/// </summary>
public interface ILivenessStore
{
    /// <summary>Mark an endpoint as alive for the given duration.</summary>
    Task SetAliveAsync(EndpointId endpointId, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>Check whether a specific endpoint is currently alive.</summary>
    Task<bool> IsAliveAsync(EndpointId endpointId, CancellationToken ct = default);

    /// <summary>
    /// Given a set of endpoint IDs, return only those that are currently alive.
    /// Batched to minimise round-trips.
    /// </summary>
    Task<IReadOnlySet<EndpointId>> FilterAliveAsync(IEnumerable<EndpointId> endpointIds, CancellationToken ct = default);

    /// <summary>Remove a liveness entry immediately (e.g. on explicit deregistration).</summary>
    Task RemoveAsync(EndpointId endpointId, CancellationToken ct = default);

    /// <summary>Remove all liveness entries for a given agent's endpoints.</summary>
    Task RemoveAllForAgentAsync(IEnumerable<EndpointId> endpointIds, CancellationToken ct = default);
}
