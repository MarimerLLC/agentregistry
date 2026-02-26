using AgentRegistry.Application.Agents;
using DomainEndpoint = AgentRegistry.Domain.Agents.Endpoint;
using AgentRegistry.Domain.Agents;

namespace AgentRegistry.Api.Agents.Models;

public record AgentResponse(
    string Id,
    string Name,
    string? Description,
    string OwnerId,
    Dictionary<string, string> Labels,
    List<CapabilityResponse> Capabilities,
    List<EndpointResponse> Endpoints,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static AgentResponse From(Agent agent) => new(
        agent.Id.ToString(),
        agent.Name,
        agent.Description,
        agent.OwnerId,
        new Dictionary<string, string>(agent.Labels),
        agent.Capabilities.Select(CapabilityResponse.From).ToList(),
        agent.Endpoints.Select(e => EndpointResponse.From(e, isLive: null)).ToList(),
        agent.CreatedAt,
        agent.UpdatedAt);

    public static AgentResponse From(AgentWithLiveness agentWithLiveness) => new(
        agentWithLiveness.Agent.Id.ToString(),
        agentWithLiveness.Agent.Name,
        agentWithLiveness.Agent.Description,
        agentWithLiveness.Agent.OwnerId,
        new Dictionary<string, string>(agentWithLiveness.Agent.Labels),
        agentWithLiveness.Agent.Capabilities.Select(CapabilityResponse.From).ToList(),
        agentWithLiveness.Agent.Endpoints
            .Select(e => EndpointResponse.From(e, agentWithLiveness.LiveEndpointIds.Contains(e.Id)))
            .ToList(),
        agentWithLiveness.Agent.CreatedAt,
        agentWithLiveness.Agent.UpdatedAt);
}

public record CapabilityResponse(string Id, string Name, string? Description, List<string> Tags)
{
    public static CapabilityResponse From(Capability c) =>
        new(c.Id.ToString(), c.Name, c.Description, c.Tags.ToList());
}

public record EndpointResponse(
    string Id,
    string Name,
    string Transport,
    string Protocol,
    string Address,
    string LivenessModel,
    int? TtlSeconds,
    int? HeartbeatIntervalSeconds,
    bool? IsLive,
    string? ProtocolMetadata)
{
    public static EndpointResponse From(DomainEndpoint e, bool? isLive) => new(
        e.Id.ToString(),
        e.Name,
        e.Transport.ToString(),
        e.Protocol.ToString(),
        e.Address,
        e.LivenessModel.ToString(),
        e.TtlDuration.HasValue ? (int)e.TtlDuration.Value.TotalSeconds : null,
        e.HeartbeatInterval.HasValue ? (int)e.HeartbeatInterval.Value.TotalSeconds : null,
        isLive,
        e.ProtocolMetadata);
}

public record PagedAgentResponse(
    List<AgentResponse> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages,
    bool HasNextPage,
    bool HasPreviousPage);
