using MarimerLLC.AgentRegistry.Domain.Agents;

namespace MarimerLLC.AgentRegistry.Application.Agents;

public interface IAgentRepository
{
    Task<Agent?> FindByIdAsync(AgentId id, CancellationToken ct = default);
    Task<PagedResult<Agent>> SearchAsync(AgentSearchFilter filter, CancellationToken ct = default);
    Task AddAsync(Agent agent, CancellationToken ct = default);
    Task UpdateAsync(Agent agent, CancellationToken ct = default);
    Task<bool> DeleteAsync(AgentId id, CancellationToken ct = default);
    Task<IReadOnlyList<Endpoint>> GetEphemeralEndpointsAliveAfterAsync(
        DateTimeOffset since, CancellationToken ct = default);
}
