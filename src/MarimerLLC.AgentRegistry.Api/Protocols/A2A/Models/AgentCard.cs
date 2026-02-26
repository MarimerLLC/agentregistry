using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MarimerLLC.AgentRegistry.Api.Protocols.A2A.Models;

/// <summary>
/// A2A Agent Card — v1.0 RC.
/// Served at /.well-known/agent.json on the agent's own host,
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

    [JsonPropertyName("supportedInterfaces")]
    public required IReadOnlyList<AgentInterface> SupportedInterfaces { get; init; }

    [JsonPropertyName("defaultInputModes")]
    public required IReadOnlyList<string> DefaultInputModes { get; init; }

    [JsonPropertyName("defaultOutputModes")]
    public required IReadOnlyList<string> DefaultOutputModes { get; init; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }

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
    /// OpenAPI 3.0-style security scheme definitions, keyed by scheme name.
    /// Stored as raw JSON to accommodate the variety of scheme types.
    /// </summary>
    [JsonPropertyName("securitySchemes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? SecuritySchemes { get; init; }

    [JsonPropertyName("securityRequirements")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<JsonObject>? SecurityRequirements { get; init; }
}

public record AgentCapabilities
{
    [JsonPropertyName("streaming")]
    public bool Streaming { get; init; }

    [JsonPropertyName("pushNotifications")]
    public bool PushNotifications { get; init; }

    [JsonPropertyName("extendedAgentCard")]
    public bool ExtendedAgentCard { get; init; }
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
}

/// <summary>Transport binding for the agent. Transport is one of "JSONRPC", "GRPC", "HTTP".</summary>
public record AgentInterface
{
    [JsonPropertyName("url")]
    public required string Url { get; init; }

    [JsonPropertyName("transport")]
    public required string Transport { get; init; }
}

public record AgentProvider
{
    [JsonPropertyName("organization")]
    public required string Organization { get; init; }

    [JsonPropertyName("url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; init; }
}

/// <summary>Request body for A2A-native agent registration.</summary>
public record RegisterViaCardRequest(AgentCard Card);
