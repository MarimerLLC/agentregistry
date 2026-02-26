using AgentRegistry.Domain.ApiKeys;

namespace AgentRegistry.Application.Auth;

public record ApiKeyValidationResult(bool IsValid, string? OwnerId, string? KeyId, ApiKeyScope? Scope);

public record ApiKeyInfo(
    string Id,
    string OwnerId,
    string? Description,
    ApiKeyScope Scope,
    string KeyPrefix,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastUsedAt,
    bool IsActive);

public interface IApiKeyService
{
    /// <summary>Validate a raw key. Updates last_used_at on success.</summary>
    Task<ApiKeyValidationResult> ValidateAsync(string rawKey, CancellationToken ct = default);

    /// <summary>Issue a new API key with the given scope. Returns the raw key — shown exactly once.</summary>
    Task<(string RawKey, string KeyId)> IssueAsync(string ownerId, string? description, ApiKeyScope scope, CancellationToken ct = default);

    /// <summary>List all keys for an owner (without raw key values).</summary>
    Task<IReadOnlyList<ApiKeyInfo>> ListAsync(string ownerId, CancellationToken ct = default);

    /// <summary>Revoke a key by ID. Throws if the key doesn't belong to the owner.</summary>
    Task RevokeAsync(string keyId, string requestingOwnerId, CancellationToken ct = default);
}
