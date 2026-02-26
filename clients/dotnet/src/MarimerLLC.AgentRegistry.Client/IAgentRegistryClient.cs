namespace MarimerLLC.AgentRegistry.Client;

public interface IAgentRegistryClient
{
    Task<AgentResponse> RegisterAsync(RegisterAgentRequest request, CancellationToken ct = default);
    Task<AgentResponse?> GetAgentAsync(Guid agentId, CancellationToken ct = default);
    Task<AgentResponse> UpdateAgentAsync(Guid agentId, UpdateAgentRequest request, CancellationToken ct = default);
    Task DeregisterAsync(Guid agentId, CancellationToken ct = default);
    Task<EndpointResponse> AddEndpointAsync(Guid agentId, EndpointRequest request, CancellationToken ct = default);
    Task RemoveEndpointAsync(Guid agentId, Guid endpointId, CancellationToken ct = default);
    Task HeartbeatAsync(Guid agentId, Guid endpointId, CancellationToken ct = default);
    Task RenewAsync(Guid agentId, Guid endpointId, CancellationToken ct = default);
    Task<PagedAgentResponse> DiscoverAsync(DiscoveryFilter? filter = null, CancellationToken ct = default);
}
