namespace MarimerLLC.AgentRegistry.Client;

public record RegisterAgentRequest(
    string Name,
    string? Description = null,
    Dictionary<string, string>? Labels = null,
    List<CapabilityRequest>? Capabilities = null,
    List<EndpointRequest>? Endpoints = null);

public record CapabilityRequest(
    string Name,
    string? Description = null,
    List<string>? Tags = null);

public record EndpointRequest(
    string Name,
    TransportType Transport,
    ProtocolType Protocol,
    string Address,
    LivenessModel LivenessModel,
    int? TtlSeconds = null,
    int? HeartbeatIntervalSeconds = null,
    string? ProtocolMetadata = null);

public record UpdateAgentRequest(
    string Name,
    string? Description = null,
    Dictionary<string, string>? Labels = null);
