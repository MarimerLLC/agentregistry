using System.Text.Json;
using AgentRegistry.Api.Protocols.ACP.Models;
using AgentRegistry.Application.Agents;
using AgentRegistry.Domain.Agents;
using DomainEndpoint = AgentRegistry.Domain.Agents.Endpoint;

namespace AgentRegistry.Api.Protocols.ACP;

public static class AcpAgentManifestMapper
{
    private static readonly IReadOnlyList<string> DefaultContentTypes = ["text/plain", "application/json"];

    // ── Domain → AcpAgentManifest ─────────────────────────────────────────────

    /// <summary>
    /// Build an ACP agent manifest from a registry agent. Returns null if the agent
    /// has no ACP endpoints.
    /// </summary>
    public static AcpAgentManifest? ToManifest(AgentWithLiveness agentWithLiveness)
    {
        var agent = agentWithLiveness.Agent;

        var acpEndpoints = agent.Endpoints
            .Where(e => e.Protocol == ProtocolType.ACP && e.Transport == TransportType.Http)
            .ToList();

        if (acpEndpoints.Count == 0) return null;

        var primary = acpEndpoints.FirstOrDefault(e =>
            agentWithLiveness.LiveEndpointIds.Contains(e.Id)) ?? acpEndpoints[0];

        // Read stored ACP metadata for round-tripping.
        StoredAcpMetadata? stored = null;
        if (!string.IsNullOrWhiteSpace(primary.ProtocolMetadata))
        {
            try
            {
                stored = JsonSerializer.Deserialize<StoredAcpMetadata>(
                    primary.ProtocolMetadata,
                    JsonSerializerOptions.Web);
            }
            catch (JsonException) { /* build from domain only */ }
        }

        var capabilities = agent.Capabilities
            .Select(c => new AcpCapability { Name = c.Name, Description = c.Description })
            .ToList();

        // Merge any stored capabilities not already present by name.
        if (stored?.Capabilities is { Count: > 0 })
        {
            var existing = capabilities.Select(c => c.Name).ToHashSet();
            foreach (var cap in stored.Capabilities.Where(c => !existing.Contains(c.Name)))
                capabilities.Add(cap);
        }

        var allTags = agent.Capabilities.SelectMany(c => c.Tags).ToHashSet();
        if (stored?.Metadata?.Tags is { Count: > 0 })
            foreach (var t in stored.Metadata.Tags) allTags.Add(t);

        return new AcpAgentManifest
        {
            Id = agent.Id.ToString(),
            Name = ToAcpName(agent.Name),
            Description = agent.Description ?? agent.Name,
            InputContentTypes = stored?.InputContentTypes ?? DefaultContentTypes,
            OutputContentTypes = stored?.OutputContentTypes ?? DefaultContentTypes,
            EndpointUrl = primary.Address,
            IsLive = agentWithLiveness.LiveEndpointIds.Contains(primary.Id),
            Metadata = new AcpMetadata
            {
                Capabilities = capabilities.Count > 0 ? capabilities : null,
                Tags = allTags.Count > 0 ? allTags.ToList() : null,
                Domains = stored?.Metadata?.Domains,
                Framework = stored?.Metadata?.Framework,
                ProgrammingLanguage = stored?.Metadata?.ProgrammingLanguage,
                NaturalLanguages = stored?.Metadata?.NaturalLanguages,
                License = stored?.Metadata?.License,
                Documentation = stored?.Metadata?.Documentation,
                Author = stored?.Metadata?.Author,
                Annotations = stored?.Metadata?.Annotations,
                InputSchema = stored?.Metadata?.InputSchema,
                OutputSchema = stored?.Metadata?.OutputSchema,
                ConfigSchema = stored?.Metadata?.ConfigSchema,
                ThreadStateSchema = stored?.Metadata?.ThreadStateSchema,
            },
            Status = stored?.Status,
        };
    }

    // ── AcpAgentManifest → domain ─────────────────────────────────────────────

    public record MappedRegistration(
        string Name,
        string Description,
        IEnumerable<RegisterCapabilityRequest> Capabilities,
        IEnumerable<RegisterEndpointRequest> Endpoints);

    /// <summary>
    /// Map an ACP agent manifest to the inputs needed for AgentService.RegisterAsync.
    /// The full manifest (including JSON Schemas) is serialised into ProtocolMetadata.
    /// </summary>
    public static MappedRegistration FromManifest(AcpAgentManifest manifest, string endpointUrl)
    {
        // Merge tags from metadata with the "acp" marker tag.
        var baseTags = (manifest.Metadata?.Tags ?? []).Append("acp").ToList();

        var capabilities = (manifest.Metadata?.Capabilities ?? [])
            .Select(c => new RegisterCapabilityRequest(
                c.Name,
                c.Description,
                [..baseTags, "capability"]))
            .ToList();

        // If no explicit capabilities but we have content-type info, create one.
        if (capabilities.Count == 0)
            capabilities.Add(new RegisterCapabilityRequest(
                manifest.Name, manifest.Description, baseTags));

        var metadata = JsonSerializer.Serialize(new StoredAcpMetadata
        {
            InputContentTypes = manifest.InputContentTypes.ToList(),
            OutputContentTypes = manifest.OutputContentTypes.ToList(),
            Capabilities = manifest.Metadata?.Capabilities?.ToList(),
            Metadata = manifest.Metadata,
            Status = manifest.Status,
        }, JsonSerializerOptions.Web);

        var endpoints = new[]
        {
            new RegisterEndpointRequest(
                Name: "acp-http",
                Transport: TransportType.Http,
                Protocol: ProtocolType.ACP,
                Address: endpointUrl,
                LivenessModel: LivenessModel.Persistent,
                TtlDuration: null,
                HeartbeatInterval: TimeSpan.FromSeconds(30),
                ProtocolMetadata: metadata)
        };

        return new MappedRegistration(manifest.Name, manifest.Description, capabilities, endpoints);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Convert an arbitrary agent name to an RFC 1123 DNS-label-compatible string.
    /// Lowercases, replaces spaces and underscores with hyphens, strips other characters.
    /// </summary>
    public static string ToAcpName(string name) =>
        System.Text.RegularExpressions.Regex
            .Replace(name.ToLowerInvariant().Replace(' ', '-').Replace('_', '-'), @"[^a-z0-9\-]", "")
            .Trim('-');

    // ── Stored metadata shape ─────────────────────────────────────────────────

    private record StoredAcpMetadata
    {
        public List<string>? InputContentTypes { get; init; }
        public List<string>? OutputContentTypes { get; init; }
        public List<AcpCapability>? Capabilities { get; init; }
        public AcpMetadata? Metadata { get; init; }
        public AcpStatus? Status { get; init; }
    }
}
