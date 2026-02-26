using MarimerLLC.AgentRegistry.Domain.Agents;

namespace MarimerLLC.AgentRegistry.Application.Agents;

public record AgentSearchFilter(
    string? CapabilityName = null,
    IReadOnlyList<string>? Tags = null,
    ProtocolType? Protocol = null,
    TransportType? Transport = null,
    string? OwnerId = null,
    bool LiveOnly = true,
    int Page = 1,
    int PageSize = 20);
