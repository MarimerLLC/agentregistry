using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AgentRegistry.Api.Protocols.ACP.Models;

/// <summary>
/// ACP Agent Manifest — spec 0.2.0 (i-am-bee/acp).
/// Registry adds <see cref="Id"/>, <see cref="EndpointUrl"/>, and <see cref="IsLive"/>
/// as discovery-layer annotations; these fields are not part of the ACP spec itself.
/// </summary>
public record AcpAgentManifest
{
    /// <summary>RFC 1123 DNS-label agent name (lowercase alphanumeric and hyphens).</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>MIME types this agent accepts as input. Supports wildcards (e.g. "text/*").</summary>
    [JsonPropertyName("input_content_types")]
    public required IReadOnlyList<string> InputContentTypes { get; init; }

    /// <summary>MIME types this agent produces as output.</summary>
    [JsonPropertyName("output_content_types")]
    public required IReadOnlyList<string> OutputContentTypes { get; init; }

    [JsonPropertyName("metadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AcpMetadata? Metadata { get; init; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AcpStatus? Status { get; init; }

    // ── Registry-added fields ─────────────────────────────────────────────────

    /// <summary>Registry-assigned ID for this agent.</summary>
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }

    /// <summary>Base URL of the agent's ACP server (e.g. https://agent.example.com).</summary>
    [JsonPropertyName("endpoint_url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EndpointUrl { get; init; }

    /// <summary>Whether the agent's ACP endpoint is currently live.</summary>
    [JsonPropertyName("is_live")]
    public bool IsLive { get; init; }
}

public record AcpMetadata
{
    [JsonPropertyName("capabilities")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AcpCapability>? Capabilities { get; init; }

    [JsonPropertyName("tags")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Tags { get; init; }

    [JsonPropertyName("domains")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Domains { get; init; }

    [JsonPropertyName("framework")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Framework { get; init; }

    [JsonPropertyName("programming_language")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProgrammingLanguage { get; init; }

    [JsonPropertyName("natural_languages")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? NaturalLanguages { get; init; }

    [JsonPropertyName("license")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? License { get; init; }

    [JsonPropertyName("documentation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Documentation { get; init; }

    [JsonPropertyName("author")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AcpAuthor? Author { get; init; }

    [JsonPropertyName("annotations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Annotations { get; init; }

    /// <summary>JSON Schema for the agent's expected input structure.</summary>
    [JsonPropertyName("input_schema")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? InputSchema { get; init; }

    /// <summary>JSON Schema for the agent's output structure.</summary>
    [JsonPropertyName("output_schema")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? OutputSchema { get; init; }

    /// <summary>JSON Schema for agent configuration parameters.</summary>
    [JsonPropertyName("config_schema")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? ConfigSchema { get; init; }

    /// <summary>JSON Schema for persistent conversation/session state.</summary>
    [JsonPropertyName("thread_state_schema")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? ThreadStateSchema { get; init; }
}

public record AcpCapability
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }
}

public record AcpAuthor
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }

    [JsonPropertyName("email")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Email { get; init; }

    [JsonPropertyName("url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; init; }
}

public record AcpStatus
{
    [JsonPropertyName("avg_run_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? AvgRunTokens { get; init; }

    [JsonPropertyName("avg_run_time_seconds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? AvgRunTimeSeconds { get; init; }

    [JsonPropertyName("success_rate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? SuccessRate { get; init; }
}

/// <summary>Request body for ACP-native agent registration.</summary>
public record RegisterViaAcpRequest(AcpAgentManifest Manifest, string EndpointUrl);
