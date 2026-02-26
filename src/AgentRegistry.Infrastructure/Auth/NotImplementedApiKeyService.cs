using AgentRegistry.Application.Auth;
using AgentRegistry.Domain.ApiKeys;

namespace AgentRegistry.Infrastructure.Auth;

/// <summary>
/// Placeholder registered until the API key persistence layer is implemented.
/// All methods throw — callers using JWT auth are unaffected.
/// </summary>
public class NotImplementedApiKeyService : IApiKeyService
{
    public Task<ApiKeyValidationResult> ValidateAsync(string rawKey, CancellationToken ct = default) =>
        throw new NotImplementedException("API key authentication is not yet implemented.");

    public Task<(string RawKey, string KeyId)> IssueAsync(string ownerId, string? description, ApiKeyScope scope, CancellationToken ct = default) =>
        throw new NotImplementedException("API key issuance is not yet implemented.");

    public Task<IReadOnlyList<ApiKeyInfo>> ListAsync(string ownerId, CancellationToken ct = default) =>
        throw new NotImplementedException("API key listing is not yet implemented.");

    public Task RevokeAsync(string keyId, string requestingOwnerId, CancellationToken ct = default) =>
        throw new NotImplementedException("API key revocation is not yet implemented.");
}
