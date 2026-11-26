using System.Text.Json;
using System.Text.Json.Nodes;
using MarimerLLC.AgentRegistry.Api.Protocols.A2A.Models;
using MarimerLLC.AgentRegistry.Application.Agents;
using MarimerLLC.AgentRegistry.Domain.Agents;
using DomainEndpoint = MarimerLLC.AgentRegistry.Domain.Agents.Endpoint;

namespace MarimerLLC.AgentRegistry.Api.Protocols.A2A;

public static class A2AAgentCardMapper
{
    private static readonly IReadOnlyList<string> DefaultModes = ["text/plain", "application/json"];

    // ── Domain → AgentCard ────────────────────────────────────────────────────

    /// <summary>
    /// Build an A2A 1.0 AgentCard from a registry agent. Returns null if the agent
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
        // When registered via POST /a2a/agents, the A2A-only card fields are stored here.
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

        // Stored skills queued per name, so capabilities that share a name each take the next
        // stored skill in order rather than all taking the first.
        var storedSkills = (stored?.Skills ?? [])
            .GroupBy(s => s.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => new Queue<AgentSkill>(g), StringComparer.Ordinal);

        var skills = agent.Capabilities.Select(c =>
        {
            // Carry the A2A skill id and A2A-only fields (examples, modes, security) over from the stored card.
            AgentSkill? original = null;
            if (storedSkills.TryGetValue(c.Name, out var queue)) queue.TryDequeue(out original);
            return new AgentSkill
            {
                Id = original?.Id ?? c.Id.ToString(),
                Name = c.Name,
                Description = c.Description ?? c.Name,
                Tags = c.Tags.ToList(),
                Examples = original?.Examples,
                InputModes = original?.InputModes,
                OutputModes = original?.OutputModes,
                SecurityRequirements = original?.SecurityRequirements,
            };
        }).ToList();

        // Merge any preserved A2A skills not matched to one of our capabilities.
        skills.AddRange(storedSkills.Values.SelectMany(q => q));

        var interfaces = a2aEndpoints.Select(e =>
        {
            var url = BuildEndpointUrl(e, registryBaseUrl);
            // Several interfaces can share a URL (e.g. JSONRPC and HTTP+JSON at one base URL), so
            // match on URL and binding — FromAgentCard names each endpoint after its binding.
            // Fall back to URL alone for endpoints that were not named that way.
            var match = e.Transport == TransportType.Http
                ? stored?.Interfaces?.FirstOrDefault(i =>
                      string.Equals(i.Url, e.Address, StringComparison.Ordinal) &&
                      string.Equals(i.ProtocolBinding, e.Name, StringComparison.Ordinal))
                  ?? stored?.Interfaces?.FirstOrDefault(i =>
                      string.Equals(i.Url, e.Address, StringComparison.Ordinal))
                : null;

            return new AgentInterface
            {
                Url = url,
                ProtocolBinding = match?.ProtocolBinding ?? DefaultBinding(e.Transport),
                ProtocolVersion = match?.ProtocolVersion ?? A2ABindings.DefaultProtocolVersion,
                Tenant = match?.Tenant,
            };
        }).ToList();

        return new AgentCard
        {
            Name = agent.Name,
            Description = agent.Description ?? agent.Name,
            Version = stored?.Version ?? "1.0",
            Capabilities = stored?.Capabilities ?? new AgentCapabilities(),
            Skills = skills,
            SupportedInterfaces = interfaces,
            DefaultInputModes = stored?.DefaultInputModes ?? DefaultModes,
            DefaultOutputModes = stored?.DefaultOutputModes ?? DefaultModes,
            // provider.url is required in A2A 1.0; rows stored without one drop the provider.
            Provider = stored?.Provider is { Organization: { Length: > 0 } org, Url: { Length: > 0 } providerUrl }
                ? new AgentProvider { Organization = org, Url = providerUrl }
                : null,
            DocumentationUrl = stored?.DocumentationUrl,
            IconUrl = stored?.IconUrl,
            SecuritySchemes = stored?.SecuritySchemes,
            SecurityRequirements = stored?.SecurityRequirements,
            // Signatures are deliberately not re-served: this card is rebuilt, so they would not verify.
        };
    }

    // ── AgentCard → domain ────────────────────────────────────────────────────

    public record MappedRegistration(
        string Name,
        string Description,
        IEnumerable<RegisterCapabilityRequest> Capabilities,
        IEnumerable<RegisterEndpointRequest> Endpoints);

    /// <summary>
    /// Map an A2A 1.0 AgentCard to the inputs needed for AgentService.RegisterAsync.
    /// A2A-only card fields are serialised into ProtocolMetadata so discovery can rebuild the card.
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
            Interfaces = card.SupportedInterfaces.Select(i => new StoredInterface
            {
                Url = i.Url,
                ProtocolBinding = i.ProtocolBinding,
                ProtocolVersion = i.ProtocolVersion,
                Tenant = i.Tenant,
            }).ToList(),
            DefaultInputModes = card.DefaultInputModes.ToList(),
            DefaultOutputModes = card.DefaultOutputModes.ToList(),
            Provider = card.Provider is null
                ? null
                : new StoredProvider { Organization = card.Provider.Organization, Url = card.Provider.Url },
            DocumentationUrl = card.DocumentationUrl,
            IconUrl = card.IconUrl,
            SecuritySchemes = card.SecuritySchemes,
            SecurityRequirements = card.SecurityRequirements?.ToList(),
            Signatures = card.Signatures?.ToList(),
        }, JsonSerializerOptions.Web);

        var endpoints = card.SupportedInterfaces.Select(iface => new RegisterEndpointRequest(
            Name: iface.ProtocolBinding,
            Transport: FromA2ABinding(iface.ProtocolBinding),
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

    /// <summary>The registry's own A2A agent card, served at /.well-known/agent-card.json.</summary>
    public static AgentCard RegistrySelfCard(string baseUrl) => new()
    {
        Name = "AgentRegistry",
        Description = "Protocol-agnostic registry for AI agents. Supports A2A, MCP, and ACP over HTTP, AMQP, and Azure Service Bus.",
        Version = RegistryIdentity.Version,
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
            new AgentInterface
            {
                Url = $"{baseUrl}/a2a/agents",
                ProtocolBinding = A2ABindings.JsonRpc,
                ProtocolVersion = A2ABindings.DefaultProtocolVersion,
            },
        ],
        DefaultInputModes = ["application/json"],
        DefaultOutputModes = ["application/json"],
        Provider = new AgentProvider
        {
            Organization = RegistryIdentity.ProviderOrganization,
            Url = RegistryIdentity.ProviderUrl,
        },
        DocumentationUrl = $"{baseUrl}/scalar/v1",
    };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildEndpointUrl(DomainEndpoint endpoint, string registryBaseUrl) =>
        endpoint.Transport == TransportType.Http
            ? endpoint.Address          // absolute URL provided by the agent
            : $"{registryBaseUrl}/a2a/queue/{endpoint.Id}"; // synthetic URL for queue endpoints

    /// <summary>
    /// Binding for an endpoint with no recorded interface (generic-API registrations, rows
    /// stored before A2A 1.0, queue endpoints). Queue bindings are registry-specific names.
    /// </summary>
    private static string DefaultBinding(TransportType transport) => transport switch
    {
        TransportType.Amqp => A2ABindings.Amqp,
        TransportType.AzureServiceBus => A2ABindings.AzureServiceBus,
        _ => A2ABindings.JsonRpc,
    };

    private static TransportType FromA2ABinding(string binding) => binding switch
    {
        A2ABindings.JsonRpc or A2ABindings.Grpc or A2ABindings.HttpJson => TransportType.Http,
        A2ABindings.Amqp => TransportType.Amqp,
        A2ABindings.AzureServiceBus => TransportType.AzureServiceBus,
        _ => TransportType.Http,
    };

    // ── Stored metadata shape ─────────────────────────────────────────────────

    /// <summary>
    /// A2A-specific fields that have no equivalent in the generic domain model.
    /// Serialised into Endpoint.ProtocolMetadata so cards round-trip correctly.
    /// Members are optional so rows written before A2A 1.0 still deserialise.
    /// </summary>
    private record StoredA2AMetadata
    {
        public string Version { get; init; } = "1.0";
        public AgentCapabilities? Capabilities { get; init; }
        public List<AgentSkill>? Skills { get; init; }
        public List<StoredInterface>? Interfaces { get; init; }
        public List<string>? DefaultInputModes { get; init; }
        public List<string>? DefaultOutputModes { get; init; }
        public StoredProvider? Provider { get; init; }
        public string? DocumentationUrl { get; init; }
        public string? IconUrl { get; init; }
        public JsonObject? SecuritySchemes { get; init; }
        public List<JsonObject>? SecurityRequirements { get; init; }

        /// <summary>Kept for the record; never re-served (see <see cref="AgentCard.Signatures"/>).</summary>
        public List<AgentCardSignature>? Signatures { get; init; }
    }

    private record StoredInterface
    {
        public string? Url { get; init; }
        public string? ProtocolBinding { get; init; }
        public string? ProtocolVersion { get; init; }
        public string? Tenant { get; init; }
    }

    private record StoredProvider
    {
        public string? Organization { get; init; }
        public string? Url { get; init; }
    }
}
