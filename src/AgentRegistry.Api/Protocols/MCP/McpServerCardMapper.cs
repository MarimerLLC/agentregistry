using System.Text.Json;
using AgentRegistry.Api.Protocols.MCP.Models;
using AgentRegistry.Application.Agents;
using AgentRegistry.Domain.Agents;
using DomainEndpoint = AgentRegistry.Domain.Agents.Endpoint;

namespace AgentRegistry.Api.Protocols.MCP;

public static class McpServerCardMapper
{
    private const string LatestMcpVersion = "2025-11-25";

    // ── Domain → McpServerCard ────────────────────────────────────────────────

    /// <summary>
    /// Build an MCP server card from a registry agent. Returns null if the agent
    /// has no MCP endpoints.
    /// </summary>
    public static McpServerCard? ToServerCard(AgentWithLiveness agentWithLiveness)
    {
        var agent = agentWithLiveness.Agent;

        var mcpEndpoints = agent.Endpoints
            .Where(e => e.Protocol == ProtocolType.MCP && e.Transport == TransportType.Http)
            .ToList();

        if (mcpEndpoints.Count == 0) return null;

        // Prefer a live endpoint; fall back to any MCP endpoint.
        var primary = mcpEndpoints.FirstOrDefault(e =>
            agentWithLiveness.LiveEndpointIds.Contains(e.Id)) ?? mcpEndpoints[0];

        // Read stored MCP metadata from ProtocolMetadata if present.
        StoredMcpMetadata? stored = null;
        if (!string.IsNullOrWhiteSpace(primary.ProtocolMetadata))
        {
            try
            {
                stored = JsonSerializer.Deserialize<StoredMcpMetadata>(
                    primary.ProtocolMetadata,
                    JsonSerializerOptions.Web);
            }
            catch (JsonException) { /* build from domain only */ }
        }

        // Build capabilities from domain + stored metadata.
        var capabilities = stored?.Capabilities ?? InferCapabilities(agent);

        return new McpServerCard
        {
            Id = agent.Id.ToString(),
            McpVersion = stored?.McpVersion ?? LatestMcpVersion,
            ServerInfo = new McpServerInfo
            {
                Name = agent.Name,
                Version = stored?.ServerVersion ?? "1.0.0",
            },
            Endpoints = new McpEndpointMap
            {
                StreamableHttp = primary.Address,
            },
            Capabilities = capabilities,
            Instructions = stored?.Instructions ?? agent.Description,
            Tools = stored?.Tools,
            Resources = stored?.Resources,
            Prompts = stored?.Prompts,
            Authentication = stored?.Authentication,
            IsLive = agentWithLiveness.LiveEndpointIds.Contains(primary.Id),
        };
    }

    // ── McpServerCard → domain ────────────────────────────────────────────────

    public record MappedRegistration(
        string Name,
        string? Description,
        IEnumerable<RegisterCapabilityRequest> Capabilities,
        IEnumerable<RegisterEndpointRequest> Endpoints);

    /// <summary>
    /// Map an MCP server card to the inputs needed for AgentService.RegisterAsync.
    /// The full card metadata is serialised into ProtocolMetadata for round-tripping.
    /// </summary>
    public static MappedRegistration FromServerCard(McpServerCard card)
    {
        var capabilities = BuildCapabilities(card);

        var metadata = JsonSerializer.Serialize(new StoredMcpMetadata
        {
            McpVersion = card.McpVersion,
            ServerVersion = card.ServerInfo.Version,
            Capabilities = card.Capabilities,
            Instructions = card.Instructions,
            Tools = card.Tools?.ToList(),
            Resources = card.Resources?.ToList(),
            Prompts = card.Prompts?.ToList(),
            Authentication = card.Authentication,
        }, JsonSerializerOptions.Web);

        var endpoints = new List<RegisterEndpointRequest>();

        if (card.Endpoints.StreamableHttp is not null)
        {
            endpoints.Add(new RegisterEndpointRequest(
                Name: "streamable-http",
                Transport: TransportType.Http,
                Protocol: ProtocolType.MCP,
                Address: card.Endpoints.StreamableHttp,
                LivenessModel: LivenessModel.Persistent,
                TtlDuration: null,
                HeartbeatInterval: TimeSpan.FromSeconds(30),
                ProtocolMetadata: metadata));
        }

        return new MappedRegistration(
            card.ServerInfo.Name,
            card.Instructions,
            capabilities,
            endpoints);
    }

    // ── Capability inference ──────────────────────────────────────────────────

    private static IEnumerable<RegisterCapabilityRequest> BuildCapabilities(McpServerCard card)
    {
        // Prefer explicit tool/resource/prompt descriptors if provided.
        if (card.Tools is { Count: > 0 })
            foreach (var t in card.Tools)
                yield return new RegisterCapabilityRequest(t.Name, t.Description, ["tool", "mcp"]);

        if (card.Resources is { Count: > 0 })
            foreach (var r in card.Resources)
                yield return new RegisterCapabilityRequest(r.Name, r.Description, ["resource", "mcp"]);

        if (card.Prompts is { Count: > 0 })
            foreach (var p in card.Prompts)
                yield return new RegisterCapabilityRequest(p.Name, p.Description, ["prompt", "mcp"]);

        // Fall back to capability-level declarations.
        if (card.Tools is null or { Count: 0 } && card.Capabilities.Tools is not null)
            yield return new RegisterCapabilityRequest("tools", "Exposes callable tools", ["tool", "mcp"]);

        if (card.Resources is null or { Count: 0 } && card.Capabilities.Resources is not null)
            yield return new RegisterCapabilityRequest("resources", "Exposes readable resources", ["resource", "mcp"]);

        if (card.Prompts is null or { Count: 0 } && card.Capabilities.Prompts is not null)
            yield return new RegisterCapabilityRequest("prompts", "Exposes prompt templates", ["prompt", "mcp"]);
    }

    private static McpCapabilities InferCapabilities(Agent agent)
    {
        var tags = agent.Capabilities.SelectMany(c => c.Tags).ToHashSet();
        return new McpCapabilities
        {
            Tools = tags.Contains("tool") ? new McpToolsCapability() : null,
            Resources = tags.Contains("resource") ? new McpResourcesCapability() : null,
            Prompts = tags.Contains("prompt") ? new McpPromptsCapability() : null,
        };
    }

    // ── Stored metadata shape ─────────────────────────────────────────────────

    private record StoredMcpMetadata
    {
        public string McpVersion { get; init; } = LatestMcpVersion;
        public string ServerVersion { get; init; } = "1.0.0";
        public McpCapabilities? Capabilities { get; init; }
        public string? Instructions { get; init; }
        public List<McpToolDescriptor>? Tools { get; init; }
        public List<McpResourceDescriptor>? Resources { get; init; }
        public List<McpPromptDescriptor>? Prompts { get; init; }
        public System.Text.Json.Nodes.JsonObject? Authentication { get; init; }
    }
}
