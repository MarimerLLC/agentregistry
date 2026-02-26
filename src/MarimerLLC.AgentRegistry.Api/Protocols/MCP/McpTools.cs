using System.ComponentModel;
using System.Text.Json;
using MarimerLLC.AgentRegistry.Api.Protocols.A2A;
using MarimerLLC.AgentRegistry.Api.Protocols.ACP;
using MarimerLLC.AgentRegistry.Application.Agents;
using MarimerLLC.AgentRegistry.Domain.Agents;
using ModelContextProtocol.Server;

namespace MarimerLLC.AgentRegistry.Api.Protocols.MCP;

/// <summary>
/// MCP tools that expose the registry's discovery capabilities to MCP clients.
/// All tools are read-only — discovery is public in AgentRegistry.
/// Registered at POST/GET /mcp (Streamable HTTP transport, spec 2025-11-25).
/// </summary>
[McpServerToolType]
public sealed class AgentRegistryMcpTools(AgentService agentService)
{
    // ── Discovery ─────────────────────────────────────────────────────────────

    [McpServerTool]
    [Description("Discover registered AI agents. Returns live agents matching the given filters. " +
                 "Agents may speak A2A, MCP, ACP, or other protocols over HTTP or message queues.")]
    public async Task<string> DiscoverAgents(
        [Description("Filter by capability name (partial match, case-insensitive).")] string? capability = null,
        [Description("Comma-separated tags to filter by (e.g. 'nlp,tool'). All tags must match.")] string? tags = null,
        [Description("Filter by protocol: A2A, MCP, ACP, or Unknown.")] string? protocol = null,
        [Description("Filter by transport: Http, Amqp, or AzureServiceBus.")] string? transport = null,
        [Description("Only return agents with live endpoints. Default: true.")] bool liveOnly = true,
        [Description("Page number (1-based). Default: 1.")] int page = 1,
        [Description("Results per page (max 100). Default: 20.")] int pageSize = 20,
        CancellationToken ct = default)
    {
        ProtocolType? protocolType = null;
        if (protocol is not null && Enum.TryParse<ProtocolType>(protocol, ignoreCase: true, out var p))
            protocolType = p;

        TransportType? transportType = null;
        if (transport is not null && Enum.TryParse<TransportType>(transport, ignoreCase: true, out var t))
            transportType = t;

        var tagList = tags?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var filter = new AgentSearchFilter(
            CapabilityName: capability,
            Tags: tagList,
            Protocol: protocolType,
            Transport: transportType,
            LiveOnly: liveOnly,
            Page: Math.Max(1, page),
            PageSize: Math.Clamp(pageSize, 1, 100));

        var result = await agentService.DiscoverAsync(filter, ct);

        var summary = new
        {
            totalCount = result.TotalCount,
            page = result.Page,
            pageSize = result.PageSize,
            totalPages = result.TotalPages,
            hasNextPage = result.HasNextPage,
            agents = result.Items.Select(a => new
            {
                id = a.Agent.Id.ToString(),
                name = a.Agent.Name,
                description = a.Agent.Description,
                capabilities = a.Agent.Capabilities.Select(c => new { c.Name, c.Description, c.Tags }),
                endpoints = a.Agent.Endpoints.Select(e => new
                {
                    id = e.Id.ToString(),
                    e.Name,
                    transport = e.Transport.ToString(),
                    protocol = e.Protocol.ToString(),
                    e.Address,
                    livenessModel = e.LivenessModel.ToString(),
                    isLive = a.LiveEndpointIds.Contains(e.Id),
                }),
            }),
        };

        return JsonSerializer.Serialize(summary, JsonSerializerOptions.Web);
    }

    [McpServerTool]
    [Description("Get full details for a specific registered agent by its registry ID (UUID).")]
    public async Task<string> GetAgent(
        [Description("The agent's registry ID (UUID format).")] string id,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var guid))
            return JsonSerializer.Serialize(new { error = $"'{id}' is not a valid agent ID." }, JsonSerializerOptions.Web);

        var result = await agentService.GetByIdWithLivenessAsync(new AgentId(guid), ct);
        if (result is null)
            return JsonSerializer.Serialize(new { error = $"Agent '{id}' not found." }, JsonSerializerOptions.Web);

        var detail = new
        {
            id = result.Agent.Id.ToString(),
            name = result.Agent.Name,
            description = result.Agent.Description,
            ownerId = result.Agent.OwnerId,
            labels = result.Agent.Labels,
            createdAt = result.Agent.CreatedAt,
            updatedAt = result.Agent.UpdatedAt,
            capabilities = result.Agent.Capabilities.Select(c => new
            {
                id = c.Id.ToString(),
                c.Name,
                c.Description,
                c.Tags,
            }),
            endpoints = result.Agent.Endpoints.Select(e => new
            {
                id = e.Id.ToString(),
                e.Name,
                transport = e.Transport.ToString(),
                protocol = e.Protocol.ToString(),
                e.Address,
                livenessModel = e.LivenessModel.ToString(),
                ttlSeconds = e.TtlDuration?.TotalSeconds,
                heartbeatIntervalSeconds = e.HeartbeatInterval?.TotalSeconds,
                isLive = result.LiveEndpointIds.Contains(e.Id),
            }),
        };

        return JsonSerializer.Serialize(detail, JsonSerializerOptions.Web);
    }

    // ── Protocol card retrieval ───────────────────────────────────────────────

    [McpServerTool]
    [Description("Get the A2A agent card for a registered agent. " +
                 "Returns null if the agent has no A2A endpoints. " +
                 "The card follows the A2A v1.0 RC spec.")]
    public async Task<string> GetA2ACard(
        [Description("The agent's registry ID (UUID format).")] string id,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var guid))
            return JsonSerializer.Serialize(new { error = $"'{id}' is not a valid agent ID." }, JsonSerializerOptions.Web);

        var result = await agentService.GetByIdWithLivenessAsync(new AgentId(guid), ct);
        if (result is null)
            return JsonSerializer.Serialize(new { error = $"Agent '{id}' not found." }, JsonSerializerOptions.Web);

        var card = A2AAgentCardMapper.ToAgentCard(result, "");
        if (card is null)
            return JsonSerializer.Serialize(new { error = $"Agent '{id}' has no A2A endpoints." }, JsonSerializerOptions.Web);

        return JsonSerializer.Serialize(card, JsonSerializerOptions.Web);
    }

    [McpServerTool]
    [Description("Get the MCP server card for a registered MCP server. " +
                 "Returns null if the agent has no MCP (HTTP) endpoints. " +
                 "The card follows the MCP spec 2025-11-25.")]
    public async Task<string> GetMcpServerCard(
        [Description("The agent's registry ID (UUID format).")] string id,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var guid))
            return JsonSerializer.Serialize(new { error = $"'{id}' is not a valid agent ID." }, JsonSerializerOptions.Web);

        var result = await agentService.GetByIdWithLivenessAsync(new AgentId(guid), ct);
        if (result is null)
            return JsonSerializer.Serialize(new { error = $"Agent '{id}' not found." }, JsonSerializerOptions.Web);

        var card = McpServerCardMapper.ToServerCard(result);
        if (card is null)
            return JsonSerializer.Serialize(new { error = $"Agent '{id}' has no MCP (HTTP) endpoints." }, JsonSerializerOptions.Web);

        return JsonSerializer.Serialize(card, JsonSerializerOptions.Web);
    }

    [McpServerTool]
    [Description("Get the ACP agent manifest for a registered ACP agent. " +
                 "Returns null if the agent has no ACP endpoints. " +
                 "The manifest follows the ACP spec 0.2.0 (IBM/BeeAI).")]
    public async Task<string> GetAcpManifest(
        [Description("The agent's registry ID (UUID format).")] string id,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var guid))
            return JsonSerializer.Serialize(new { error = $"'{id}' is not a valid agent ID." }, JsonSerializerOptions.Web);

        var result = await agentService.GetByIdWithLivenessAsync(new AgentId(guid), ct);
        if (result is null)
            return JsonSerializer.Serialize(new { error = $"Agent '{id}' not found." }, JsonSerializerOptions.Web);

        var manifest = AcpAgentManifestMapper.ToManifest(result);
        if (manifest is null)
            return JsonSerializer.Serialize(new { error = $"Agent '{id}' has no ACP endpoints." }, JsonSerializerOptions.Web);

        return JsonSerializer.Serialize(manifest, JsonSerializerOptions.Web);
    }
}
