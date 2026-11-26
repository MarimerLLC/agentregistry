using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MarimerLLC.AgentRegistry.Api.Protocols.MCP.Models;
using MarimerLLC.AgentRegistry.Application.Agents;
using MarimerLLC.AgentRegistry.Domain.Agents;
using DomainEndpoint = MarimerLLC.AgentRegistry.Domain.Agents.Endpoint;

namespace MarimerLLC.AgentRegistry.Api.Protocols.MCP;

public static partial class McpServerCardMapper
{
    private const int MaxDescriptionLength = 100;
    private const int MaxNameLength = 200;
    private const int MaxVersionLength = 255;

    // ── Domain → ServerCard ───────────────────────────────────────────────────

    /// <summary>
    /// Build a SEP-2127 Server Card from a registry agent. Returns null if the agent
    /// has no MCP (HTTP) endpoints.
    /// </summary>
    public static ServerCard? ToServerCard(AgentWithLiveness agentWithLiveness)
    {
        var agent = agentWithLiveness.Agent;

        var mcpEndpoints = agent.Endpoints
            .Where(e => e.Protocol == ProtocolType.MCP && e.Transport == TransportType.Http)
            .ToList();

        if (mcpEndpoints.Count == 0) return null;

        // Prefer a live endpoint's metadata; all endpoints registered from one card share it.
        var primary = mcpEndpoints.FirstOrDefault(e =>
            agentWithLiveness.LiveEndpointIds.Contains(e.Id)) ?? mcpEndpoints[0];

        var stored = ReadStored(primary);
        var storedCard = stored?.Card;
        var storedMeta = storedCard is null ? null : ReadRegistryMeta(storedCard);

        // Pre-SEP-2127 rows kept the protocol version and descriptors at the top level.
        IReadOnlyList<string>? legacyVersions = stored?.McpVersion is { Length: > 0 } v ? [v] : null;

        // Live endpoints first (stable within each group) so a client taking remotes[0] reaches a live one.
        var remotes = mcpEndpoints
            .OrderByDescending(e => agentWithLiveness.LiveEndpointIds.Contains(e.Id))
            .Select(e =>
        {
            var match = storedCard?.Remotes?.FirstOrDefault(r =>
                r.Type == ServerCardConstants.StreamableHttp &&
                string.Equals(r.Url, e.Address, StringComparison.Ordinal));

            return new ServerCardRemote
            {
                Type = ServerCardConstants.StreamableHttp,
                Url = e.Address,
                Headers = match?.Headers,
                Variables = match?.Variables,
                SupportedProtocolVersions = match?.SupportedProtocolVersions ?? legacyVersions,
                ExtensionsInfo = match?.ExtensionsInfo,
            };
        }).ToList();

        var registryMeta = new RegistryServerMeta
        {
            Id = agent.Id.ToString(),
            IsLive = mcpEndpoints.Any(e => agentWithLiveness.LiveEndpointIds.Contains(e.Id)),
            Instructions = storedMeta?.Instructions ?? stored?.Instructions,
            Tools = storedMeta?.Tools ?? stored?.Tools,
            Resources = storedMeta?.Resources ?? stored?.Resources,
            Prompts = storedMeta?.Prompts ?? stored?.Prompts,
            Authentication = storedMeta?.Authentication ?? stored?.Authentication,
        };

        // Keep any other _meta keys the server published; replace the registry block.
        var meta = storedCard?.Meta?.DeepClone().AsObject() ?? new JsonObject();
        meta[ServerCardConstants.RegistryMetaKey] =
            JsonSerializer.SerializeToNode(registryMeta, JsonSerializerOptions.Web);

        return new ServerCard
        {
            Schema = ServerCardConstants.SchemaUrl,
            Name = storedCard?.Name is { Length: > 0 } n ? n : DerivedName(agent.Id),
            Version = storedCard?.Version is { Length: > 0 } ver ? ver
                : stored?.ServerVersion is { Length: > 0 } legacyVer ? legacyVer
                : "1.0.0",
            Description = Truncate(agent.Description is { Length: > 0 } d ? d : agent.Name, MaxDescriptionLength),
            Title = Truncate(agent.Name, MaxDescriptionLength),
            WebsiteUrl = storedCard?.WebsiteUrl,
            Repository = storedCard?.Repository,
            Icons = storedCard?.Icons,
            Remotes = remotes,
            Meta = meta,
        };
    }

    /// <summary>Read the registry <c>_meta</c> block from a card, or null if absent or malformed.</summary>
    public static RegistryServerMeta? ReadRegistryMeta(ServerCard card)
    {
        if (card.Meta?[ServerCardConstants.RegistryMetaKey] is not JsonObject node) return null;
        try
        {
            return node.Deserialize<RegistryServerMeta>(JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Card name for agents registered without one (e.g. through the generic API).</summary>
    public static string DerivedName(AgentId id) => $"agentregistry/{id.Value:N}";

    // ── ServerCard → domain ───────────────────────────────────────────────────

    public record MappedRegistration(
        string Name,
        string? Description,
        IEnumerable<RegisterCapabilityRequest> Capabilities,
        IEnumerable<RegisterEndpointRequest> Endpoints);

    /// <summary>Outcome of mapping a submitted card: a registration, or validation errors.</summary>
    public record MappingResult(MappedRegistration? Registration, Dictionary<string, string[]>? Errors)
    {
        public bool IsValid => Errors is null;
    }

    /// <summary>
    /// Validate a SEP-2127 Server Card and map it to the inputs needed for
    /// AgentService.RegisterAsync. The card (with the registry <c>_meta</c> block) is
    /// stored in ProtocolMetadata so it round-trips.
    /// </summary>
    public static MappingResult FromServerCard(ServerCard card)
    {
        var errors = Validate(card);
        if (errors.Count > 0) return new MappingResult(null, errors);

        var registryMeta = ReadRegistryMeta(card);

        // Store the card without the per-response fields the registry computes.
        var storedMeta = card.Meta?.DeepClone().AsObject() ?? new JsonObject();
        if (registryMeta is not null)
            storedMeta[ServerCardConstants.RegistryMetaKey] = JsonSerializer.SerializeToNode(
                registryMeta with { Id = null, IsLive = null }, JsonSerializerOptions.Web);

        var storedCard = card with { Meta = storedMeta.Count > 0 ? storedMeta : null };
        var metadata = JsonSerializer.Serialize(
            new StoredMcpMetadata { Card = storedCard }, JsonSerializerOptions.Web);

        var endpoints = HttpRemotes(card)
            .Select(r => r.Url)
            .Distinct(StringComparer.Ordinal)
            .Select(url => new RegisterEndpointRequest(
                Name: ServerCardConstants.StreamableHttp,
                Transport: TransportType.Http,
                Protocol: ProtocolType.MCP,
                Address: url,
                LivenessModel: LivenessModel.Persistent,
                TtlDuration: null,
                HeartbeatInterval: TimeSpan.FromSeconds(30),
                ProtocolMetadata: metadata))
            .ToList();

        var registration = new MappedRegistration(
            card.Title is { Length: > 0 } t ? t : card.Name,
            card.Description,
            BuildCapabilities(registryMeta).ToList(),
            endpoints);

        return new MappingResult(registration, null);
    }

    private static Dictionary<string, string[]> Validate(ServerCard card)
    {
        var errors = new Dictionary<string, string[]>();

        if (card.Schema != ServerCardConstants.SchemaUrl)
            errors["$schema"] = [$"Must be '{ServerCardConstants.SchemaUrl}'."];

        if (card.Name.Length is < 3 or > MaxNameLength || !NameRegex().IsMatch(card.Name))
            errors["name"] = ["Must be a reverse-DNS name with exactly one slash, e.g. 'com.example/weather' (3–200 characters)."];

        // Lengths count code points, as JSON Schema maxLength does.
        if (string.IsNullOrWhiteSpace(card.Version) || CodePointLength(card.Version) > MaxVersionLength)
            errors["version"] = ["Required, at most 255 characters."];

        if (CodePointLength(card.Description) is < 1 or > MaxDescriptionLength)
            errors["description"] = ["Required, 1–100 characters."];

        if (card.Title is not null && CodePointLength(card.Title) is < 1 or > MaxDescriptionLength)
            errors["title"] = ["When present, 1–100 characters."];

        if (!HttpRemotes(card).Any())
            errors["remotes"] = ["At least one 'streamable-http' remote with an http(s) URL is required. The deprecated 'sse' transport is not supported."];

        // A registry block that is present must parse; otherwise its tools would be silently dropped.
        if (card.Meta?.TryGetPropertyValue(ServerCardConstants.RegistryMetaKey, out var block) == true &&
            block is not null && ReadRegistryMeta(card) is null)
            errors["_meta"] = [$"'{ServerCardConstants.RegistryMetaKey}' must be an object whose tools, resources and prompts each have the required fields (tools/prompts: name; resources: uri and name)."];

        return errors;
    }

    /// <summary>
    /// Streamable HTTP remotes with a concrete http(s) URL. SSE remotes are ignored. The scheme
    /// is checked as written, case-sensitively, to match the schema's <c>^https?://[^\s]+</c>.
    /// </summary>
    private static IEnumerable<ServerCardRemote> HttpRemotes(ServerCard card) =>
        (card.Remotes ?? []).Where(r =>
            r.Type == ServerCardConstants.StreamableHttp &&
            (r.Url.StartsWith("http://", StringComparison.Ordinal) ||
             r.Url.StartsWith("https://", StringComparison.Ordinal)) &&
            !r.Url.Any(char.IsWhiteSpace) &&
            Uri.TryCreate(r.Url, UriKind.Absolute, out _));

    private static IEnumerable<RegisterCapabilityRequest> BuildCapabilities(RegistryServerMeta? meta)
    {
        if (meta is null) yield break;

        foreach (var t in meta.Tools ?? [])
            yield return new RegisterCapabilityRequest(t.Name, t.Description, ["tool", "mcp"]);

        foreach (var r in meta.Resources ?? [])
            yield return new RegisterCapabilityRequest(r.Name, r.Description, ["resource", "mcp"]);

        foreach (var p in meta.Prompts ?? [])
            yield return new RegisterCapabilityRequest(p.Name, p.Description, ["prompt", "mcp"]);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static StoredMcpMetadata? ReadStored(DomainEndpoint endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint.ProtocolMetadata)) return null;
        try
        {
            return JsonSerializer.Deserialize<StoredMcpMetadata>(endpoint.ProtocolMetadata, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null; // malformed metadata — build from domain only
        }
    }

    private static int CodePointLength(string value)
    {
        var count = 0;
        foreach (var _ in value.EnumerateRunes()) count++;
        return count;
    }

    /// <summary>Truncate to <paramref name="max"/> code points, never splitting a surrogate pair.</summary>
    private static string Truncate(string value, int max)
    {
        if (CodePointLength(value) <= max) return value;

        var builder = new StringBuilder();
        var count = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (count == max - 1) break;
            builder.Append(rune.ToString());
            count++;
        }
        return builder.Append('…').ToString();
    }

    // ServerCardConstants.NamePattern with \z instead of $: in .NET, $ also matches before a
    // trailing newline, which the schema's ECMA regex does not allow.
    [GeneratedRegex(@"^[a-zA-Z0-9.-]+/[a-zA-Z0-9._-]+\z")]
    private static partial Regex NameRegex();

    // ── Stored metadata shape ─────────────────────────────────────────────────

    /// <summary>
    /// What MCP registrations keep in Endpoint.ProtocolMetadata. New rows hold the submitted
    /// card; the other members are read from rows written before SEP-2127 cards.
    /// </summary>
    private record StoredMcpMetadata
    {
        public ServerCard? Card { get; init; }

        // Legacy (pre-SEP-2127) shape — read only.
        public string? McpVersion { get; init; }
        public string? ServerVersion { get; init; }
        public string? Instructions { get; init; }
        public List<McpToolDescriptor>? Tools { get; init; }
        public List<McpResourceDescriptor>? Resources { get; init; }
        public List<McpPromptDescriptor>? Prompts { get; init; }
        public JsonObject? Authentication { get; init; }
    }
}
