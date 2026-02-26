using MarimerLLC.AgentRegistry.Domain.ApiKeys;

namespace MarimerLLC.AgentRegistry.Api.Auth;

/// <summary>Claim type and value constants used across the registry's authorization policies.</summary>
public static class RegistryClaims
{
    /// <summary>Claim type set on all API key-authenticated principals.</summary>
    public const string Scope = "registry_scope";

    public static class Scopes
    {
        public static readonly string Admin = ApiKeyScope.Admin.ToString();
        public static readonly string Agent = ApiKeyScope.Agent.ToString();
    }
}
