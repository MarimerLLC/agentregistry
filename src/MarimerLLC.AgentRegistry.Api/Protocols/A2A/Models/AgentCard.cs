using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MarimerLLC.AgentRegistry.Api.Protocols.A2A.Models;

/// <summary>A2A 1.0 protocol binding names for <see cref="AgentInterface.ProtocolBinding"/>.</summary>
public static class A2ABindings
{
    public const string JsonRpc = "JSONRPC";
    public const string Grpc = "GRPC";
    public const string HttpJson = "HTTP+JSON";

    /// <summary>Registry-specific binding for agents reached over AMQP (not an A2A 1.0 core binding).</summary>
    public const string Amqp = "AMQP";

    /// <summary>Registry-specific binding for agents reached over Azure Service Bus (not an A2A 1.0 core binding).</summary>
    public const string AzureServiceBus = "AzureServiceBus";

    /// <summary>The A2A protocol version the registry assumes when none was recorded.</summary>
    public const string DefaultProtocolVersion = "1.0";
}

/// <summary>
/// A2A 1.0 Agent Card.
/// Served at /.well-known/agent-card.json on the agent's own host,
/// and at /a2a/agents/{id} on the registry for each registered agent.
/// </summary>
public record AgentCard
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("capabilities")]
    public required AgentCapabilities Capabilities { get; init; }

    [JsonPropertyName("skills")]
    public required IReadOnlyList<AgentSkill> Skills { get; init; }

    /// <summary>Ordered list of ways to reach the agent; the first entry is preferred.</summary>
    [JsonPropertyName("supportedInterfaces")]
    public required IReadOnlyList<AgentInterface> SupportedInterfaces { get; init; }

    [JsonPropertyName("defaultInputModes")]
    public required IReadOnlyList<string> DefaultInputModes { get; init; }

    [JsonPropertyName("defaultOutputModes")]
    public required IReadOnlyList<string> DefaultOutputModes { get; init; }

    [JsonPropertyName("provider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AgentProvider? Provider { get; init; }

    [JsonPropertyName("documentationUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DocumentationUrl { get; init; }

    [JsonPropertyName("iconUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IconUrl { get; init; }

    /// <summary>
    /// Security scheme definitions, keyed by scheme name.
    /// Stored as raw JSON to accommodate the variety of scheme types.
    /// </summary>
    [JsonPropertyName("securitySchemes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? SecuritySchemes { get; init; }

    [JsonPropertyName("securityRequirements")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<JsonObject>? SecurityRequirements { get; init; }

    /// <summary>
    /// JWS signatures over the card. Accepted on registration but never re-served: the
    /// registry rebuilds cards, so a stored signature would no longer verify.
    /// </summary>
    [JsonPropertyName("signatures")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AgentCardSignature>? Signatures { get; init; }
}

public record AgentCapabilities
{
    [JsonPropertyName("streaming")]
    public bool Streaming { get; init; }

    [JsonPropertyName("pushNotifications")]
    public bool PushNotifications { get; init; }

    [JsonPropertyName("extendedAgentCard")]
    public bool ExtendedAgentCard { get; init; }

    [JsonPropertyName("extensions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AgentExtension>? Extensions { get; init; }
}

/// <summary>A protocol extension the agent supports.</summary>
public record AgentExtension
{
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    /// <summary>Whether clients must understand this extension to use the agent.</summary>
    [JsonPropertyName("required")]
    public bool Required { get; init; }

    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? Params { get; init; }
}

/// <summary>A JWS signature over the agent card (RFC 7515, JSON serialization).</summary>
public record AgentCardSignature
{
    /// <summary>Base64url-encoded JWS protected header.</summary>
    [JsonPropertyName("protected")]
    public required string Protected { get; init; }

    /// <summary>Base64url-encoded signature.</summary>
    [JsonPropertyName("signature")]
    public required string Signature { get; init; }

    /// <summary>Unprotected JWS header values.</summary>
    [JsonPropertyName("header")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? Header { get; init; }
}

public record AgentSkill
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("tags")]
    public required IReadOnlyList<string> Tags { get; init; }

    [JsonPropertyName("examples")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Examples { get; init; }

    [JsonPropertyName("inputModes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? InputModes { get; init; }

    [JsonPropertyName("outputModes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? OutputModes { get; init; }

    /// <summary>Security requirements specific to this skill.</summary>
    [JsonPropertyName("securityRequirements")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<JsonObject>? SecurityRequirements { get; init; }
}

/// <summary>One way to reach the agent: a URL plus the protocol binding and A2A version spoken there.</summary>
public record AgentInterface
{
    [JsonPropertyName("url")]
    public required string Url { get; init; }

    /// <summary><c>JSONRPC</c>, <c>GRPC</c> or <c>HTTP+JSON</c> (see <see cref="A2ABindings"/>).</summary>
    [JsonPropertyName("protocolBinding")]
    public required string ProtocolBinding { get; init; }

    /// <summary>A2A protocol version, e.g. <c>1.0</c>.</summary>
    [JsonPropertyName("protocolVersion")]
    public required string ProtocolVersion { get; init; }

    [JsonPropertyName("tenant")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Tenant { get; init; }
}

public record AgentProvider
{
    [JsonPropertyName("organization")]
    public required string Organization { get; init; }

    [JsonPropertyName("url")]
    public required string Url { get; init; }
}

/// <summary>Request body for A2A-native agent registration.</summary>
public record RegisterViaCardRequest(AgentCard Card);
