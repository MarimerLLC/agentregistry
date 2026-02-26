using MarimerLLC.AgentRegistry.Domain.Agents;

namespace MarimerLLC.AgentRegistry.Api.Agents.Models;

public record RegisterAgentRequest(
    string Name,
    string? Description,
    Dictionary<string, string>? Labels,
    List<CapabilityRequest>? Capabilities,
    List<EndpointRequest>? Endpoints);

public record CapabilityRequest(
    string Name,
    string? Description,
    List<string>? Tags);

public record EndpointRequest(
    string Name,
    TransportType Transport,
    ProtocolType Protocol,
    string Address,
    LivenessModel LivenessModel,
    int? TtlSeconds,
    int? HeartbeatIntervalSeconds,
    string? ProtocolMetadata);

public record UpdateAgentRequest(
    string Name,
    string? Description,
    Dictionary<string, string>? Labels);
