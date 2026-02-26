using System.Security.Cryptography;
using System.Text;

namespace MarimerLLC.AgentRegistry.Domain.ApiKeys;

public class ApiKey
{
    public ApiKeyId Id { get; }
    public string OwnerId { get; }
    public string? Description { get; }
    public ApiKeyScope Scope { get; }

    /// <summary>SHA-256 hex digest of the raw key. Never stored in plaintext.</summary>
    public string KeyHash { get; }

    /// <summary>First 10 characters of the raw key (e.g. "ar_Abc123De"). Safe to display.</summary>
    public string KeyPrefix { get; }

    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public DateTimeOffset? LastUsedAt { get; private set; }

    public bool IsActive => RevokedAt is null;

    private ApiKey(ApiKeyId id, string ownerId, string? description, ApiKeyScope scope, string keyHash, string keyPrefix)
    {
        Id = id;
        OwnerId = ownerId;
        Description = description;
        Scope = scope;
        KeyHash = keyHash;
        KeyPrefix = keyPrefix;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    // EF Core constructor
    private ApiKey() { Id = default; OwnerId = null!; KeyHash = null!; KeyPrefix = null!; }

    /// <summary>
    /// Creates a new API key entity and returns it alongside the raw key value.
    /// The raw key is shown exactly once — the caller is responsible for returning it to the user.
    /// </summary>
    public static (ApiKey Entity, string RawKey) Generate(string ownerId, string? description, ApiKeyScope scope)
    {
        // 32 random bytes → base64url → 43-char string. Full key: "ar_<43chars>" = 46 chars.
        var randomBytes = RandomNumberGenerator.GetBytes(32);
        var rawKey = "ar_" + Convert.ToBase64String(randomBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        var keyHash = ComputeHash(rawKey);
        var keyPrefix = rawKey[..10];

        var entity = new ApiKey(ApiKeyId.New(), ownerId, description, scope, keyHash, keyPrefix);
        return (entity, rawKey);
    }

    public static string ComputeHash(string rawKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey))).ToLowerInvariant();

    public void Revoke()
    {
        if (!IsActive) throw new InvalidOperationException("Key is already revoked.");
        RevokedAt = DateTimeOffset.UtcNow;
    }

    public void RecordUsage() => LastUsedAt = DateTimeOffset.UtcNow;
}
