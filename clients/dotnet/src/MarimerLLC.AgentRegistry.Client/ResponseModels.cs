namespace MarimerLLC.AgentRegistry.Client;

public record AgentResponse(
    string Id,
    string Name,
    string? Description,
    string OwnerId,
    Dictionary<string, string> Labels,
    List<CapabilityResponse> Capabilities,
    List<EndpointResponse> Endpoints,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record CapabilityResponse(
    string Id,
    string Name,
    string? Description,
    List<string> Tags);

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
    string? ProtocolMetadata);

public record PagedAgentResponse(
    List<AgentResponse> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages,
    bool HasNextPage,
    bool HasPreviousPage);
