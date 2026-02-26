namespace MarimerLLC.AgentRegistry.Client;

public class AgentHeartbeatOptions
{
    /// <summary>
    /// If set, the service registers this agent on startup and deregisters on shutdown.
    /// </summary>
    public RegisterAgentRequest? Registration { get; set; }

    /// <summary>
    /// How often to send heartbeats to Persistent endpoints. Defaults to 30 seconds.
    /// Should be meaningfully less than the endpoint's HeartbeatIntervalSeconds to
    /// avoid the registry marking the endpoint stale between beats.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether to call DeregisterAsync when the service stops. Defaults to true.
    /// Set to false for rolling restarts where you want the registration to persist.
    /// </summary>
    public bool DeregisterOnStop { get; set; } = true;
}
