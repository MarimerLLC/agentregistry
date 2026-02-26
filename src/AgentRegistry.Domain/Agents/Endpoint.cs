namespace AgentRegistry.Domain.Agents;

/// <summary>
/// A reachable address where an agent accepts work.
/// An agent may expose multiple endpoints using different transports or protocols.
/// </summary>
public class Endpoint
{
    public EndpointId Id { get; }
    public AgentId AgentId { get; }

    /// <summary>Human-readable label for this endpoint (e.g. "primary", "async-queue").</summary>
    public string Name { get; private set; }

    public TransportType Transport { get; }
    public ProtocolType Protocol { get; }

    /// <summary>
    /// For HTTP: a URL. For AMQP: a queue/exchange name. For ASB: a queue or topic name.
    /// </summary>
    public string Address { get; private set; }

    public LivenessModel LivenessModel { get; }

    /// <summary>
    /// For <see cref="LivenessModel.Ephemeral"/>: how long a registration lasts before expiring.
    /// </summary>
    public TimeSpan? TtlDuration { get; }

    /// <summary>
    /// For <see cref="LivenessModel.Persistent"/>: expected interval between heartbeats.
    /// The registry grants a grace period of 2.5× this value before marking the endpoint stale.
    /// </summary>
    public TimeSpan? HeartbeatInterval { get; }

    /// <summary>
    /// Optional protocol-specific metadata stored as raw JSON.
    /// Examples: A2A agent card fields, MCP tool manifest, ACP agent descriptor.
    /// </summary>
    public string? ProtocolMetadata { get; private set; }

    public Endpoint(
        EndpointId id,
        AgentId agentId,
        string name,
        TransportType transport,
        ProtocolType protocol,
        string address,
        LivenessModel livenessModel,
        TimeSpan? ttlDuration,
        TimeSpan? heartbeatInterval,
        string? protocolMetadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        ValidateLiveness(livenessModel, ttlDuration, heartbeatInterval);

        Id = id;
        AgentId = agentId;
        Name = name;
        Transport = transport;
        Protocol = protocol;
        Address = address;
        LivenessModel = livenessModel;
        TtlDuration = ttlDuration;
        HeartbeatInterval = heartbeatInterval;
        ProtocolMetadata = protocolMetadata;
    }

    // EF Core constructor
    private Endpoint() { Id = default; AgentId = default; Name = null!; Address = null!; }

    public void UpdateMetadata(string name, string address, string? protocolMetadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        Name = name;
        Address = address;
        ProtocolMetadata = protocolMetadata;
    }

    /// <summary>
    /// Returns the effective TTL to use in the liveness store for this endpoint.
    /// </summary>
    public TimeSpan EffectiveLivenessTtl() => LivenessModel switch
    {
        LivenessModel.Ephemeral => TtlDuration ?? TimeSpan.FromMinutes(5),
        LivenessModel.Persistent => HeartbeatInterval.HasValue
            ? HeartbeatInterval.Value * 2.5
            : TimeSpan.FromMinutes(1),
        _ => throw new InvalidOperationException($"Unknown liveness model: {LivenessModel}")
    };

    private static void ValidateLiveness(LivenessModel model, TimeSpan? ttl, TimeSpan? heartbeat)
    {
        if (model == LivenessModel.Ephemeral && ttl.HasValue && ttl.Value <= TimeSpan.Zero)
            throw new ArgumentException("TTL must be positive for ephemeral endpoints.");

        if (model == LivenessModel.Persistent && heartbeat.HasValue && heartbeat.Value <= TimeSpan.Zero)
            throw new ArgumentException("Heartbeat interval must be positive for persistent endpoints.");
    }
}
