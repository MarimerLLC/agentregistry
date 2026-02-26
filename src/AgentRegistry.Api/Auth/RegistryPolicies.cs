using Microsoft.AspNetCore.Authorization;

namespace AgentRegistry.Api.Auth;

public static class RegistryPolicies
{
    public const string AdminOnly = "AdminOnly";
    public const string AgentOrAdmin = "AgentOrAdmin";

    public static void Configure(AuthorizationOptions options)
    {
        // Full admin access — key management, all agent operations.
        // API keys: scope must be Admin.
        // JWT: token must carry a "registry_scope" claim of "Admin",
        //      or a "roles" claim of "registry.admin" (configurable at your IdP).
        options.AddPolicy(AdminOnly, policy => policy.RequireAssertion(ctx =>
            ctx.User.HasClaim(RegistryClaims.Scope, RegistryClaims.Scopes.Admin) ||
            ctx.User.HasClaim("roles", "registry.admin") ||
            ctx.User.IsInRole("registry.admin")));

        // Agent-level access — register, heartbeat, renew, discover. Cannot touch API keys.
        // Admins implicitly satisfy this policy too.
        options.AddPolicy(AgentOrAdmin, policy => policy.RequireAssertion(ctx =>
            ctx.User.HasClaim(RegistryClaims.Scope, RegistryClaims.Scopes.Admin) ||
            ctx.User.HasClaim(RegistryClaims.Scope, RegistryClaims.Scopes.Agent) ||
            ctx.User.HasClaim("roles", "registry.admin") ||
            ctx.User.HasClaim("roles", "registry.agent") ||
            ctx.User.IsInRole("registry.admin") ||
            ctx.User.IsInRole("registry.agent")));
    }
}
