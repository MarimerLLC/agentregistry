namespace MarimerLLC.AgentRegistry.Domain.Agents;

/// <summary>
/// How an endpoint signals that it is available.
/// </summary>
public enum LivenessModel
{
    /// <summary>
    /// The endpoint registers with a TTL and re-registers (or calls /renew) to stay alive.
    /// Suited to serverless / ephemeral workloads (Azure Functions, KEDA-scaled pods).
    /// The registry entry expires automatically when the TTL elapses.
    /// </summary>
    Ephemeral,

    /// <summary>
    /// The endpoint is a long-lived service that sends periodic heartbeats.
    /// The registry marks it stale if heartbeats stop arriving.
    /// </summary>
    Persistent,
}
