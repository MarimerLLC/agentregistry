using MarimerLLC.AgentRegistry.Application;
using MarimerLLC.AgentRegistry.Application.Auth;
using MarimerLLC.AgentRegistry.Domain.ApiKeys;
using MarimerLLC.AgentRegistry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MarimerLLC.AgentRegistry.Infrastructure.Auth;

public class SqlApiKeyService(AgentRegistryDbContext db) : IApiKeyService
{
    public async Task<ApiKeyValidationResult> ValidateAsync(string rawKey, CancellationToken ct = default)
    {
        var hash = ApiKey.ComputeHash(rawKey);

        var key = await db.ApiKeys
            .FirstOrDefaultAsync(k => k.KeyHash == hash && k.RevokedAt == null, ct);

        if (key is null)
            return new ApiKeyValidationResult(false, null, null, null);

        key.RecordUsage();
        await db.SaveChangesAsync(ct);

        return new ApiKeyValidationResult(true, key.OwnerId, key.Id.ToString(), key.Scope);
    }

    public async Task<(string RawKey, string KeyId)> IssueAsync(
        string ownerId, string? description, ApiKeyScope scope, CancellationToken ct = default)
    {
        var (entity, rawKey) = ApiKey.Generate(ownerId, description, scope);
        db.ApiKeys.Add(entity);
        await db.SaveChangesAsync(ct);
        return (rawKey, entity.Id.ToString());
    }

    public async Task<IReadOnlyList<ApiKeyInfo>> ListAsync(string ownerId, CancellationToken ct = default)
    {
        return await db.ApiKeys
            .Where(k => k.OwnerId == ownerId)
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => new ApiKeyInfo(
                k.Id.ToString(),
                k.OwnerId,
                k.Description,
                k.Scope,
                k.KeyPrefix,
                k.CreatedAt,
                k.RevokedAt,
                k.LastUsedAt,
                k.RevokedAt == null))
            .ToListAsync(ct);
    }

    public async Task RevokeAsync(string keyId, string requestingOwnerId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(keyId, out var guid))
            throw new NotFoundException($"API key '{keyId}' not found.");

        var key = await db.ApiKeys.FindAsync([new ApiKeyId(guid)], ct)
            ?? throw new NotFoundException($"API key '{keyId}' not found.");

        if (key.OwnerId != requestingOwnerId)
            throw new ForbiddenException($"API key '{keyId}' belongs to a different owner.");

        key.Revoke();
        await db.SaveChangesAsync(ct);
    }
}
