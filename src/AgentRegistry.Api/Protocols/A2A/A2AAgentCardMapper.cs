using System.Text.Json;
using System.Text.Json.Nodes;
using AgentRegistry.Api.Protocols.A2A.Models;
using AgentRegistry.Application.Agents;
using AgentRegistry.Domain.Agents;
using DomainEndpoint = AgentRegistry.Domain.Agents.Endpoint;

namespace AgentRegistry.Api.Protocols.A2A;

public static class A2AAgentCardMapper
{
    private static readonly IReadOnlyList<string> DefaultModes = ["text/plain", "application/json"];

    // ── Domain → AgentCard ────────────────────────────────────────────────────

    /// <summary>
    /// Build an A2A AgentCard from a registry agent. Returns null if the agent
    /// has no A2A endpoints (live or otherwise — callers filter as needed).
    /// </summary>
    public static AgentCard? ToAgentCard(AgentWithLiveness agentWithLiveness, string registryBaseUrl)
    {
        var agent = agentWithLiveness.Agent;

        var a2aEndpoints = agent.Endpoints
            .Where(e => e.Protocol == ProtocolType.A2A)
            .ToList();

        if (a2aEndpoints.Count == 0) return null;

        // Read any A2A-specific metadata stored on the first A2A endpoint.
        // When registered via POST /a2a/agents, the full original card is stored here.
        StoredA2AMetadata? stored = null;
        var primaryEndpoint = a2aEndpoints.FirstOrDefault(e =>
            agentWithLiveness.LiveEndpointIds.Contains(e.Id)) ?? a2aEndpoints[0];

        if (!string.IsNullOrWhiteSpace(primaryEndpoint.ProtocolMetadata))
        {
            try
            {
                stored = JsonSerializer.Deserialize<StoredA2AMetadata>(
                    primaryEndpoint.ProtocolMetadata,
                    JsonSerializerOptions.Web);
            }
            catch (JsonException) { /* malformed metadata — build from domain only */ }
        }

        var skills = agent.Capabilities.Select(c => new AgentSkill
        {
            Id = c.Id.ToString(),
            Name = c.Name,
            Description = c.Description ?? c.Name,
            Tags = c.Tags.ToList(),
        }).ToList();

        // Merge any preserved A2A skills not already covered by our capabilities.
        if (stored?.Skills is { Count: > 0 })
        {
            var existingIds = skills.Select(s => s.Id).ToHashSet();
            foreach (var s in stored.Skills.Where(s => !existingIds.Contains(s.Id)))
                skills.Add(s);
        }

        var interfaces = a2aEndpoints.Select(e => new AgentInterface
        {
            Url = BuildEndpointUrl(e, registryBaseUrl),
            Transport = ToA2ATransport(e.Transport),
        }).ToList();

        return new AgentCard
        {
            Id = agent.Id.ToString(),
            Name = agent.Name,
            Description = agent.Description ?? agent.Name,
            Version = stored?.Version ?? "1.0",
            Capabilities = stored?.Capabilities ?? new AgentCapabilities(),
            Skills = skills,
            SupportedInterfaces = interfaces,
            DefaultInputModes = stored?.DefaultInputModes ?? DefaultModes,
            DefaultOutputModes = stored?.DefaultOutputModes ?? DefaultModes,
            Provider = stored?.Provider,
            DocumentationUrl = stored?.DocumentationUrl,
            IconUrl = stored?.IconUrl,
            SecuritySchemes = stored?.SecuritySchemes,
            SecurityRequirements = stored?.SecurityRequirements,
        };
    }

    // ── AgentCard → domain ────────────────────────────────────────────────────

    public record MappedRegistration(
        string Name,
        string Description,
        IEnumerable<RegisterCapabilityRequest> Capabilities,
        IEnumerable<RegisterEndpointRequest> Endpoints);

    /// <summary>
    /// Map an A2A AgentCard to the inputs needed for AgentService.RegisterAsync.
    /// The full card is serialised into ProtocolMetadata so discovery can reconstruct it exactly.
    /// </summary>
    public static MappedRegistration FromAgentCard(AgentCard card)
    {
        var capabilities = card.Skills.Select(s => new RegisterCapabilityRequest(
            s.Name, s.Description, s.Tags));

        // Serialise the A2A-specific fields that our generic model doesn't have,
        // so ToAgentCard can round-trip them faithfully.
        var metadata = JsonSerializer.Serialize(new StoredA2AMetadata
        {
            Version = card.Version,
            Capabilities = card.Capabilities,
            Skills = card.Skills.ToList(),
            DefaultInputModes = card.DefaultInputModes.ToList(),
            DefaultOutputModes = card.DefaultOutputModes.ToList(),
            Provider = card.Provider,
            DocumentationUrl = card.DocumentationUrl,
            IconUrl = card.IconUrl,
            SecuritySchemes = card.SecuritySchemes,
            SecurityRequirements = card.SecurityRequirements?.ToList(),
        }, JsonSerializerOptions.Web);

        var endpoints = card.SupportedInterfaces.Select(iface => new RegisterEndpointRequest(
            Name: iface.Transport,
            Transport: FromA2ATransport(iface.Transport),
            Protocol: ProtocolType.A2A,
            Address: iface.Url,
            LivenessModel: LivenessModel.Persistent,
            TtlDuration: null,
            HeartbeatInterval: TimeSpan.FromSeconds(30),
            ProtocolMetadata: metadata));

        return new MappedRegistration(
            card.Name,
            card.Description,
            capabilities,
            endpoints);
    }

    // ── Registry self-card ────────────────────────────────────────────────────

    /// <summary>The registry's own A2A agent card, served at /.well-known/agent.json.</summary>
    public static AgentCard RegistrySelfCard(string baseUrl) => new()
    {
        Name = "AgentRegistry",
        Description = "Protocol-agnostic registry for AI agents. Supports A2A, MCP, and ACP over HTTP, AMQP, and Azure Service Bus.",
        Version = "1.0.0",
        Capabilities = new AgentCapabilities { Streaming = false, PushNotifications = false },
        Skills =
        [
            new AgentSkill
            {
                Id = "agent-registration",
                Name = "Agent Registration",
                Description = "Register, update, deregister, and heartbeat AI agents.",
                Tags = ["registry", "registration", "a2a", "mcp", "acp"],
            },
            new AgentSkill
            {
                Id = "agent-discovery",
                Name = "Agent Discovery",
                Description = "Discover live AI agents by capability, protocol, transport, or tag.",
                Tags = ["registry", "discovery", "search"],
            },
        ],
        SupportedInterfaces =
        [
            new AgentInterface { Url = $"{baseUrl}/a2a/agents", Transport = "JSONRPC" },
        ],
        DefaultInputModes = ["application/json"],
        DefaultOutputModes = ["application/json"],
        DocumentationUrl = $"{baseUrl}/scalar/v1",
    };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildEndpointUrl(DomainEndpoint endpoint, string registryBaseUrl) =>
        endpoint.Transport == TransportType.Http
            ? endpoint.Address          // absolute URL provided by the agent
            : $"{registryBaseUrl}/a2a/queue/{endpoint.Id}"; // synthetic URL for queue endpoints

    private static string ToA2ATransport(TransportType transport) => transport switch
    {
        TransportType.Http => "JSONRPC",
        TransportType.Amqp => "AMQP",
        TransportType.AzureServiceBus => "AzureServiceBus",
        _ => "JSONRPC",
    };

    private static TransportType FromA2ATransport(string transport) => transport switch
    {
        "JSONRPC" or "HTTP" or "GRPC" => TransportType.Http,
        "AMQP" => TransportType.Amqp,
        "AzureServiceBus" => TransportType.AzureServiceBus,
        _ => TransportType.Http,
    };

    // ── Stored metadata shape ─────────────────────────────────────────────────

    /// <summary>
    /// A2A-specific fields that have no equivalent in the generic domain model.
    /// Serialised into Endpoint.ProtocolMetadata so cards round-trip correctly.
    /// </summary>
    private record StoredA2AMetadata
    {
        public string Version { get; init; } = "1.0";
        public AgentCapabilities? Capabilities { get; init; }
        public List<AgentSkill>? Skills { get; init; }
        public List<string>? DefaultInputModes { get; init; }
        public List<string>? DefaultOutputModes { get; init; }
        public AgentProvider? Provider { get; init; }
        public string? DocumentationUrl { get; init; }
        public string? IconUrl { get; init; }
        public JsonObject? SecuritySchemes { get; init; }
        public List<JsonObject>? SecurityRequirements { get; init; }
    }
}
