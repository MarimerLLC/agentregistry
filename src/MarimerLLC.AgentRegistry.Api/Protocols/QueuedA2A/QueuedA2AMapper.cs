using System.Text.Json;
using MarimerLLC.AgentRegistry.Api.Protocols.A2A.Models;
using MarimerLLC.AgentRegistry.Api.Protocols.QueuedA2A.Models;
using MarimerLLC.AgentRegistry.Application.Agents;
using MarimerLLC.AgentRegistry.Domain.Agents;

namespace MarimerLLC.AgentRegistry.Api.Protocols.QueuedA2A;

public static class QueuedA2AMapper
{
    private static readonly IReadOnlyList<string> DefaultModes = ["application/json"];

    // ── Domain → QueuedAgentCard ──────────────────────────────────────────────

    /// <summary>
    /// Build a <see cref="QueuedAgentCard"/> from a registry agent.
    /// Returns <c>null</c> if the agent has no A2A endpoints with a non-HTTP transport.
    /// </summary>
    public static QueuedAgentCard? ToCard(AgentWithLiveness agentWithLiveness)
    {
        var agent = agentWithLiveness.Agent;

        var queuedEndpoints = agent.Endpoints
            .Where(e => e.Protocol == ProtocolType.A2A && e.Transport != TransportType.Http)
            .ToList();

        if (queuedEndpoints.Count == 0) return null;

        // Prefer a live endpoint; fall back to the first registered.
        var primary = queuedEndpoints.FirstOrDefault(e =>
            agentWithLiveness.LiveEndpointIds.Contains(e.Id)) ?? queuedEndpoints[0];

        StoredQueuedA2AMetadata? stored = null;
        if (!string.IsNullOrWhiteSpace(primary.ProtocolMetadata))
        {
            try
            {
                stored = JsonSerializer.Deserialize<StoredQueuedA2AMetadata>(
                    primary.ProtocolMetadata,
                    JsonSerializerOptions.Web);
            }
            catch (JsonException) { /* build from domain only */ }
        }

        // Prefer the stored skills verbatim — they preserve original IDs and all metadata.
        // Fall back to building from domain capabilities only when no stored skills exist.
        var skills = stored?.Skills is { Count: > 0 }
            ? stored.Skills
            : agent.Capabilities.Select(c => new AgentSkill
            {
                Id = c.Id.ToString(),
                Name = c.Name,
                Description = c.Description ?? c.Name,
                Tags = c.Tags.ToList(),
            }).ToList<AgentSkill>();

        var queueEndpoint = stored?.QueueEndpoint ?? new Models.QueueEndpoint
        {
            Technology = ToTechnology(primary.Transport),
            TaskTopic = primary.Address,
        };

        return new QueuedAgentCard
        {
            Id = agent.Id.ToString(),
            Name = agent.Name,
            Description = agent.Description ?? agent.Name,
            Version = stored?.Version ?? "1.0",
            Skills = skills,
            DefaultInputModes = stored?.DefaultInputModes ?? DefaultModes,
            DefaultOutputModes = stored?.DefaultOutputModes ?? DefaultModes,
            QueueEndpoint = queueEndpoint,
            IsLive = agentWithLiveness.LiveEndpointIds.Contains(primary.Id),
        };
    }

    // ── QueuedAgentCard → domain ──────────────────────────────────────────────

    public record MappedRegistration(
        string Name,
        string Description,
        IEnumerable<RegisterCapabilityRequest> Capabilities,
        IEnumerable<RegisterEndpointRequest> Endpoints);

    /// <summary>
    /// Map a <see cref="QueuedAgentCard"/> to the inputs needed for
    /// <see cref="AgentService.RegisterAsync"/>. All card and queue endpoint
    /// fields are serialised into <c>ProtocolMetadata</c> so discovery can
    /// reconstruct the card exactly.
    /// </summary>
    public static MappedRegistration FromCard(QueuedAgentCard card)
    {
        var capabilities = card.Skills.Select(s =>
            new RegisterCapabilityRequest(s.Name, s.Description, s.Tags));

        var metadata = JsonSerializer.Serialize(new StoredQueuedA2AMetadata
        {
            Version = card.Version,
            Skills = card.Skills.ToList(),
            DefaultInputModes = card.DefaultInputModes.ToList(),
            DefaultOutputModes = card.DefaultOutputModes.ToList(),
            QueueEndpoint = card.QueueEndpoint,
        }, JsonSerializerOptions.Web);

        var transport = FromTechnology(card.QueueEndpoint.Technology);

        var endpoints = new[]
        {
            new RegisterEndpointRequest(
                Name: $"async-{card.QueueEndpoint.Technology}",
                Transport: transport,
                Protocol: ProtocolType.A2A,
                Address: card.QueueEndpoint.TaskTopic,
                LivenessModel: LivenessModel.Persistent,
                TtlDuration: null,
                HeartbeatInterval: TimeSpan.FromSeconds(30),
                ProtocolMetadata: metadata),
        };

        return new MappedRegistration(card.Name, card.Description, capabilities, endpoints);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ToTechnology(TransportType transport) => transport switch
    {
        TransportType.AzureServiceBus => "azure-service-bus",
        _ => "rabbitmq",
    };

    private static TransportType FromTechnology(string technology) => technology switch
    {
        "azure-service-bus" => TransportType.AzureServiceBus,
        _ => TransportType.Amqp,
    };

    // ── Stored metadata shape ─────────────────────────────────────────────────

    /// <summary>
    /// All card fields that have no equivalent in the generic domain model.
    /// Serialised into <see cref="Domain.Agents.Endpoint.ProtocolMetadata"/> for round-tripping.
    /// </summary>
    private record StoredQueuedA2AMetadata
    {
        public string Version { get; init; } = "1.0";
        public List<AgentSkill>? Skills { get; init; }
        public List<string>? DefaultInputModes { get; init; }
        public List<string>? DefaultOutputModes { get; init; }
        public Models.QueueEndpoint? QueueEndpoint { get; init; }
    }
}
