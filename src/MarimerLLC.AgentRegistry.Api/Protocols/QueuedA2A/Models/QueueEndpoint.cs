using System.Text.Json.Serialization;

namespace MarimerLLC.AgentRegistry.Api.Protocols.QueuedA2A.Models;

/// <summary>
/// Connection details for an agent reachable via an async message broker.
/// Clients publish A2A task request messages to <see cref="TaskTopic"/> and
/// subscribe on <see cref="ResponseTopic"/> for results.
/// </summary>
public record QueueEndpoint
{
    /// <summary>
    /// Identifies the broker technology. Supported values: <c>rabbitmq</c>, <c>azure-service-bus</c>.
    /// </summary>
    [JsonPropertyName("technology")]
    public required string Technology { get; init; }

    // ── AMQP / RabbitMQ fields ────────────────────────────────────────────────

    /// <summary>Broker hostname or IP address (e.g. <c>rabbitmq.example.com</c>). Used for AMQP brokers.</summary>
    [JsonPropertyName("host")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Host { get; init; }

    /// <summary>Broker port. Defaults: AMQP=5672, AMQPS=5671. Omit to use the technology default.</summary>
    [JsonPropertyName("port")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Port { get; init; }

    /// <summary>AMQP virtual host (e.g. <c>/</c>). Only relevant for RabbitMQ / AMQP.</summary>
    [JsonPropertyName("virtualHost")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VirtualHost { get; init; }

    /// <summary>AMQP exchange name (e.g. <c>rockbot</c>). Only relevant for RabbitMQ / AMQP.</summary>
    [JsonPropertyName("exchange")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Exchange { get; init; }

    // ── Routing ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Routing key or topic path that callers publish A2A task requests to.
    /// For RabbitMQ this is an AMQP routing key (e.g. <c>agent.task.ResearchAgent</c>).
    /// For Azure Service Bus this is the queue or topic path (e.g. <c>agent-tasks</c>).
    /// This is also stored in <see cref="Domain.Agents.Endpoint.Address"/>.
    /// </summary>
    [JsonPropertyName("taskTopic")]
    public required string TaskTopic { get; init; }

    /// <summary>
    /// Pattern that callers should subscribe to in order to receive responses from this agent.
    /// May include a placeholder such as <c>{callerName}</c> that the caller substitutes with its
    /// own identity (e.g. <c>agent.response.{callerName}</c>).
    /// </summary>
    [JsonPropertyName("responseTopic")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResponseTopic { get; init; }

    // ── Azure Service Bus fields ──────────────────────────────────────────────

    /// <summary>
    /// Fully-qualified Azure Service Bus namespace hostname
    /// (e.g. <c>mybus.servicebus.windows.net</c>). Only relevant for Azure Service Bus.
    /// </summary>
    [JsonPropertyName("namespace")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Namespace { get; init; }

    /// <summary>Azure Service Bus queue or topic path. Only relevant for Azure Service Bus.</summary>
    [JsonPropertyName("entityPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EntityPath { get; init; }
}
