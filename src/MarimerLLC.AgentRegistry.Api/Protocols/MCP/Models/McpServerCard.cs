using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MarimerLLC.AgentRegistry.Api.Protocols.MCP.Models;

/// <summary>Constants for MCP Server Cards (SEP-2127, <c>io.modelcontextprotocol/server-card</c>).</summary>
public static class ServerCardConstants
{
    /// <summary>The only <c>$schema</c> value a v1 Server Card may carry.</summary>
    public const string SchemaUrl = "https://static.modelcontextprotocol.io/schemas/v1/server-card.schema.json";

    /// <summary>Media type for a Server Card document.</summary>
    public const string MediaType = "application/mcp-server-card+json";

    /// <summary>Media type for an AI Catalog document.</summary>
    public const string AiCatalogMediaType = "application/ai-catalog+json";

    /// <summary>
    /// <c>_meta</c> key holding registry-specific data (registry ID, liveness, and the
    /// tool/resource/prompt descriptors that drive capability search).
    /// </summary>
    public const string RegistryMetaKey = "llc.marimer/agentregistry";

    /// <summary>Remote transport type for Streamable HTTP.</summary>
    public const string StreamableHttp = "streamable-http";

    /// <summary>Remote transport type for the deprecated HTTP+SSE transport (not supported by the registry).</summary>
    public const string Sse = "sse";

    /// <summary>Pattern for <c>name</c>: reverse-DNS namespace, one slash, server name.</summary>
    public const string NamePattern = "^[a-zA-Z0-9.-]+/[a-zA-Z0-9._-]+$";
}

/// <summary>
/// MCP Server Card (SEP-2127): a static document describing a remote MCP server's identity
/// and how to connect to it. Tools, resources and prompts are deliberately not part of the
/// card; the registry carries them under <c>_meta["llc.marimer/agentregistry"]</c>.
/// </summary>
/// <remarks>
/// Required members default to empty strings rather than being C# <c>required</c>, so a
/// request body with a missing member reaches validation and gets a 400 ProblemDetails.
/// </remarks>
public record ServerCard
{
    [JsonPropertyName("$schema")]
    public string Schema { get; init; } = "";

    /// <summary>Reverse-DNS server name, e.g. <c>com.example/weather</c>.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    /// <summary>1–100 characters.</summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; init; }

    [JsonPropertyName("websiteUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WebsiteUrl { get; init; }

    [JsonPropertyName("repository")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ServerCardRepository? Repository { get; init; }

    [JsonPropertyName("icons")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ServerCardIcon>? Icons { get; init; }

    [JsonPropertyName("remotes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ServerCardRemote>? Remotes { get; init; }

    [JsonPropertyName("_meta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? Meta { get; init; }
}

/// <summary>Connection metadata for one HTTP endpoint of an MCP server.</summary>
public record ServerCardRemote
{
    /// <summary><c>streamable-http</c> or <c>sse</c>.</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "";

    /// <summary>Endpoint URL, or a template starting with a <c>{variable}</c>.</summary>
    [JsonPropertyName("url")]
    public string Url { get; init; } = "";

    [JsonPropertyName("headers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ServerCardKeyValueInput>? Headers { get; init; }

    [JsonPropertyName("variables")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, ServerCardInput>? Variables { get; init; }

    [JsonPropertyName("supportedProtocolVersions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? SupportedProtocolVersions { get; init; }

    [JsonPropertyName("extensionsInfo")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, JsonObject>? ExtensionsInfo { get; init; }
}

/// <summary>A user-supplied or pre-set value used for remote URL variables and header values.</summary>
public record ServerCardInput
{
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("isRequired")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsRequired { get; init; }

    [JsonPropertyName("isSecret")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsSecret { get; init; }

    /// <summary><c>string</c>, <c>number</c>, <c>boolean</c> or <c>filepath</c>.</summary>
    [JsonPropertyName("format")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Format { get; init; }

    [JsonPropertyName("default")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Default { get; init; }

    [JsonPropertyName("placeholder")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Placeholder { get; init; }

    [JsonPropertyName("value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Value { get; init; }

    [JsonPropertyName("choices")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Choices { get; init; }
}

/// <summary>A named input, used for HTTP headers.</summary>
public record ServerCardKeyValueInput : ServerCardInput
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("variables")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, ServerCardInput>? Variables { get; init; }
}

public record ServerCardRepository
{
    [JsonPropertyName("url")]
    public string Url { get; init; } = "";

    /// <summary>Hosting service identifier, e.g. <c>github</c>.</summary>
    [JsonPropertyName("source")]
    public string Source { get; init; } = "";

    [JsonPropertyName("subfolder")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Subfolder { get; init; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }
}

public record ServerCardIcon
{
    [JsonPropertyName("src")]
    public string Src { get; init; } = "";

    [JsonPropertyName("mimeType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MimeType { get; init; }

    [JsonPropertyName("sizes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Sizes { get; init; }

    /// <summary><c>light</c> or <c>dark</c>.</summary>
    [JsonPropertyName("theme")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Theme { get; init; }
}

/// <summary>
/// Registry data carried in a Server Card under <c>_meta["llc.marimer/agentregistry"]</c>.
/// <see cref="Id"/> and <see cref="IsLive"/> are set by the registry on the way out and
/// ignored on registration.
/// </summary>
public record RegistryServerMeta
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }

    [JsonPropertyName("isLive")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsLive { get; init; }

    [JsonPropertyName("instructions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Instructions { get; init; }

    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<McpToolDescriptor>? Tools { get; init; }

    [JsonPropertyName("resources")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<McpResourceDescriptor>? Resources { get; init; }

    [JsonPropertyName("prompts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<McpPromptDescriptor>? Prompts { get; init; }

    /// <summary>
    /// Authentication details as the server published them. Carried over from registrations made
    /// before Server Cards, whose cards had a top-level <c>authentication</c> object.
    /// </summary>
    [JsonPropertyName("authentication")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? Authentication { get; init; }
}

/// <summary>AI Catalog document served at <c>/.well-known/ai-catalog.json</c>.</summary>
public record AiCatalog
{
    [JsonPropertyName("specVersion")]
    public string SpecVersion { get; init; } = "1.0";

    [JsonPropertyName("entries")]
    public IReadOnlyList<AiCatalogEntry> Entries { get; init; } = [];
}

public record AiCatalogEntry
{
    /// <summary>Domain-anchored identifier: <c>urn:air:{publisher}:{namespace}:{name}</c>.</summary>
    [JsonPropertyName("identifier")]
    public required string Identifier { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("url")]
    public required string Url { get; init; }
}

// ── Descriptors carried in the registry _meta block ──────────────────────────────

public record McpToolDescriptor
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    /// <summary>JSON Schema (2020-12) for the tool's input parameters.</summary>
    [JsonPropertyName("inputSchema")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? InputSchema { get; init; }

    /// <summary>JSON Schema (2020-12) for the tool's output.</summary>
    [JsonPropertyName("outputSchema")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? OutputSchema { get; init; }
}

public record McpResourceDescriptor
{
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("mimeType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MimeType { get; init; }
}

public record McpPromptDescriptor
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<McpPromptArgument>? Arguments { get; init; }
}

public record McpPromptArgument
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("required")]
    public bool Required { get; init; }
}
