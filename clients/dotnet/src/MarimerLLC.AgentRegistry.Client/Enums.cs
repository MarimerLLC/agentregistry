namespace MarimerLLC.AgentRegistry.Client;

/// <summary>Transport mechanism for an agent endpoint.</summary>
public enum TransportType
{
    Http = 0,
    Amqp = 1,
    AzureServiceBus = 2,
}

/// <summary>Agent communication protocol.</summary>
public enum ProtocolType
{
    Unknown = 0,
    A2A = 1,
    MCP = 2,
    ACP = 3,
}

/// <summary>How an endpoint signals that it is available.</summary>
public enum LivenessModel
{
    /// <summary>Registers with a TTL and calls /renew to stay alive. Suited to serverless workloads.</summary>
    Ephemeral = 0,
    /// <summary>Long-lived service that sends periodic heartbeats via /heartbeat.</summary>
    Persistent = 1,
}
