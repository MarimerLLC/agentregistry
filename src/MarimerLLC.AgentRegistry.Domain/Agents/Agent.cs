namespace MarimerLLC.AgentRegistry.Domain.Agents;

/// <summary>
/// An agent registration — the aggregate root.
/// Represents a logical agent identity that may expose multiple endpoints
/// using different transports and protocols.
/// </summary>
public class Agent
{
    public AgentId Id { get; }
    public string Name { get; private set; }
    public string? Description { get; private set; }

    /// <summary>Opaque identifier of the owner (user, organization, or service principal).</summary>
    public string OwnerId { get; }

    /// <summary>Arbitrary key/value labels for filtering and categorization.</summary>
    private readonly Dictionary<string, string> _labels;
    public IReadOnlyDictionary<string, string> Labels => _labels;

    private readonly List<Endpoint> _endpoints = [];
    public IReadOnlyList<Endpoint> Endpoints => _endpoints.AsReadOnly();

    private readonly List<Capability> _capabilities = [];
    public IReadOnlyList<Capability> Capabilities => _capabilities.AsReadOnly();

    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public Agent(AgentId id, string name, string? description, string ownerId, IDictionary<string, string>? labels = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        Id = id;
        Name = name;
        Description = description;
        OwnerId = ownerId;
        _labels = labels is not null ? new Dictionary<string, string>(labels) : [];
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    // EF Core constructor
    private Agent()
    {
        Id = default; Name = null!; OwnerId = null!;
        _labels = []; _endpoints = []; _capabilities = [];
    }

    public void Update(string name, string? description, IDictionary<string, string>? labels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Description = description;
        _labels.Clear();
        if (labels is not null)
            foreach (var (k, v) in labels) _labels[k] = v;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Endpoint AddEndpoint(
        string name,
        TransportType transport,
        ProtocolType protocol,
        string address,
        LivenessModel livenessModel,
        TimeSpan? ttlDuration,
        TimeSpan? heartbeatInterval,
        string? protocolMetadata = null)
    {
        var endpoint = new Endpoint(
            EndpointId.New(), Id, name, transport, protocol, address,
            livenessModel, ttlDuration, heartbeatInterval, protocolMetadata);
        _endpoints.Add(endpoint);
        UpdatedAt = DateTimeOffset.UtcNow;
        return endpoint;
    }

    public bool RemoveEndpoint(EndpointId endpointId)
    {
        var endpoint = _endpoints.FirstOrDefault(e => e.Id == endpointId);
        if (endpoint is null) return false;
        _endpoints.Remove(endpoint);
        UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    public Endpoint? FindEndpoint(EndpointId endpointId) =>
        _endpoints.FirstOrDefault(e => e.Id == endpointId);

    public Capability AddCapability(string name, string? description, IEnumerable<string>? tags = null)
    {
        var capability = new Capability(CapabilityId.New(), Id, name, description, tags);
        _capabilities.Add(capability);
        UpdatedAt = DateTimeOffset.UtcNow;
        return capability;
    }

    public bool RemoveCapability(CapabilityId capabilityId)
    {
        var capability = _capabilities.FirstOrDefault(c => c.Id == capabilityId);
        if (capability is null) return false;
        _capabilities.Remove(capability);
        UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }
}
