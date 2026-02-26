namespace AgentRegistry.Domain.Agents;

/// <summary>
/// A protocol-agnostic description of something an agent can do.
/// Protocol-specific manifests (MCP tool lists, A2A skill cards, etc.)
/// live in the endpoint's <see cref="Endpoint.ProtocolMetadata"/>.
/// </summary>
public class Capability
{
    public CapabilityId Id { get; }
    public AgentId AgentId { get; }
    public string Name { get; private set; }
    public string? Description { get; private set; }

    private readonly List<string> _tags;
    public IReadOnlyList<string> Tags => _tags.AsReadOnly();

    public Capability(CapabilityId id, AgentId agentId, string name, string? description, IEnumerable<string>? tags = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = id;
        AgentId = agentId;
        Name = name;
        Description = description;
        _tags = tags?.ToList() ?? [];
    }

    // EF Core constructor
    private Capability() { Id = default; AgentId = default; Name = null!; _tags = []; }

    public void Update(string name, string? description, IEnumerable<string>? tags)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Description = description;
        _tags.Clear();
        if (tags is not null) _tags.AddRange(tags);
    }
}
