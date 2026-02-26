namespace AgentRegistry.Domain.ApiKeys;

public enum ApiKeyScope
{
    /// <summary>Can register/manage agents and perform heartbeats. Cannot manage API keys.</summary>
    Agent,

    /// <summary>Full access: can issue, list, and revoke API keys in addition to all Agent operations.</summary>
    Admin,
}
