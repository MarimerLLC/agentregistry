using MarimerLLC.AgentRegistry.Domain.Agents;

namespace MarimerLLC.AgentRegistry.Infrastructure.Liveness;

/// <summary>
/// Bound to the "AgentSeeds" section in configuration.
/// Each entry describes an agent that must exist in the registry and whose ephemeral
/// endpoints are always reseeded into the liveness store on startup — regardless of
/// the 48-hour window used by <see cref="EphemeralReseedService"/>.
/// </summary>
public class AgentSeedConfig
{
    public List<AgentSeedEntry> Agents { get; set; } = [];
}

public class AgentSeedEntry
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string OwnerId { get; set; }
    public Dictionary<string, string>? Labels { get; set; }
    public List<CapabilitySeedEntry> Capabilities { get; set; } = [];
    public List<EndpointSeedEntry> Endpoints { get; set; } = [];
}

public class CapabilitySeedEntry
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public List<string>? Tags { get; set; }
}

public class EndpointSeedEntry
{
    public required string Name { get; set; }
    public TransportType Transport { get; set; }
    public ProtocolType Protocol { get; set; }
    public required string Address { get; set; }
    public LivenessModel LivenessModel { get; set; }
    public double? TtlSeconds { get; set; }
    public double? HeartbeatIntervalSeconds { get; set; }
    public string? ProtocolMetadata { get; set; }
}
