using System.Collections.Concurrent;
using AgentRegistry.Application.Auth;
using AgentRegistry.Domain.ApiKeys;

namespace AgentRegistry.Api.Tests.Infrastructure;

public class FakeApiKeyService : IApiKeyService
{
    public const string AdminKey = "test-admin-key";
    public const string AgentKey = "test-agent-key";
    public const string AdminOwnerId = "test-admin-owner";
    public const string AgentOwnerId = "test-agent-owner";

    // Keep ValidKey/OwnerId aliases so existing tests compile without changes.
    public const string ValidKey = AdminKey;
    public const string OwnerId = AdminOwnerId;

    private readonly ConcurrentDictionary<string, ApiKeyInfo> _keys = new();
    private int _keyCounter;

    public Task<ApiKeyValidationResult> ValidateAsync(string rawKey, CancellationToken ct = default)
    {
        if (rawKey == AdminKey)
            return Task.FromResult(new ApiKeyValidationResult(true, AdminOwnerId, "admin-key-id", ApiKeyScope.Admin));

        if (rawKey == AgentKey)
            return Task.FromResult(new ApiKeyValidationResult(true, AgentOwnerId, "agent-key-id", ApiKeyScope.Agent));

        return Task.FromResult(new ApiKeyValidationResult(false, null, null, null));
    }

    public Task<(string RawKey, string KeyId)> IssueAsync(
        string ownerId, string? description, ApiKeyScope scope, CancellationToken ct = default)
    {
        var id = $"key-{Interlocked.Increment(ref _keyCounter)}";
        var rawKey = $"ar_fake_{id}";
        var info = new ApiKeyInfo(id, ownerId, description, scope, rawKey[..10],
            DateTimeOffset.UtcNow, null, null, true);
        _keys[id] = info;
        return Task.FromResult((rawKey, id));
    }

    public Task<IReadOnlyList<ApiKeyInfo>> ListAsync(string ownerId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ApiKeyInfo>>(
            _keys.Values.Where(k => k.OwnerId == ownerId && k.IsActive).ToList());

    public Task RevokeAsync(string keyId, string requestingOwnerId, CancellationToken ct = default)
    {
        if (_keys.TryGetValue(keyId, out var key) && key.OwnerId == requestingOwnerId)
            _keys[keyId] = key with { IsActive = false, RevokedAt = DateTimeOffset.UtcNow };
        return Task.CompletedTask;
    }

    public void Clear() => _keys.Clear();
}
