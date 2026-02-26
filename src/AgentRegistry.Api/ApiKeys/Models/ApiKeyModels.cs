using AgentRegistry.Application.Auth;
using AgentRegistry.Domain.ApiKeys;

namespace AgentRegistry.Api.ApiKeys.Models;

public record IssueApiKeyRequest(string? Description, ApiKeyScope Scope = ApiKeyScope.Agent);

public record BootstrapApiKeyRequest(string OwnerId, string? Description);

public record IssueApiKeyResponse(
    string Id,
    string OwnerId,
    string? Description,
    ApiKeyScope Scope,
    string KeyPrefix,
    string RawKey,
    DateTimeOffset CreatedAt)
{
    public static IssueApiKeyResponse From(string rawKey, ApiKeyInfo info) => new(
        info.Id, info.OwnerId, info.Description, info.Scope, info.KeyPrefix, rawKey, info.CreatedAt);
}

public record ApiKeyResponse(
    string Id,
    string OwnerId,
    string? Description,
    ApiKeyScope Scope,
    string KeyPrefix,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastUsedAt,
    bool IsActive)
{
    public static ApiKeyResponse From(ApiKeyInfo info) => new(
        info.Id, info.OwnerId, info.Description, info.Scope, info.KeyPrefix,
        info.CreatedAt, info.RevokedAt, info.LastUsedAt, info.IsActive);
}
