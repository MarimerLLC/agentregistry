using System.Text.Json.Serialization;
using MarimerLLC.AgentRegistry.Api.Protocols.A2A.Models;

namespace MarimerLLC.AgentRegistry.Api.Protocols.QueuedA2A.Models;

/// <summary>
/// An A2A agent card for agents that communicate via async message queues rather than HTTP.
/// The A2A wire protocol (task request / status update / result message shapes) is unchanged;
/// only the transport differs — callers publish messages to <see cref="QueueEndpoint.TaskTopic"/>
/// on the named broker instead of sending HTTP requests.
/// </summary>
public record QueuedAgentCard
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    /// <summary>Skills (capabilities) this agent declares, in A2A format.</summary>
    [JsonPropertyName("skills")]
    public required IReadOnlyList<AgentSkill> Skills { get; init; }

    [JsonPropertyName("defaultInputModes")]
    public required IReadOnlyList<string> DefaultInputModes { get; init; }

    [JsonPropertyName("defaultOutputModes")]
    public required IReadOnlyList<string> DefaultOutputModes { get; init; }

    /// <summary>
    /// Queue / broker connection details required to send tasks to this agent.
    /// Must be supplied on registration; always present on discovery responses.
    /// </summary>
    [JsonPropertyName("queueEndpoint")]
    public required QueueEndpoint QueueEndpoint { get; init; }

    // ── Registry-added discovery annotations ──────────────────────────────────

    /// <summary>Registry-assigned agent ID. Populated on discovery responses; omit when registering.</summary>
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }

    /// <summary>Whether the agent's queue endpoint is currently considered live by the registry.</summary>
    [JsonPropertyName("isLive")]
    public bool IsLive { get; init; }
}
