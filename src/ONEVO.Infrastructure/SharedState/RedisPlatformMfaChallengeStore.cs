using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using StackExchange.Redis;

namespace ONEVO.Infrastructure.SharedState;

/// <summary>
/// Task 21: admin MFA challenges in Redis, so the password step and the code step may land on different instances.
/// One hash per challenge, keyed by the challenge's hash (the raw challenge never reaches Redis), expiring at the
/// challenge's own expiry. Counting a failed attempt and consuming are single scripts, so two instances can never
/// both consume one challenge or both miss the attempt limit.
/// </summary>
public sealed class RedisPlatformMfaChallengeStore(
    IConnectionMultiplexer redis, ISecureTokenGenerator tokens, IDateTimeProvider clock) : IPlatformMfaChallengeStore
{
    private const string UserField = "user";
    private const string ExpiresField = "expires";
    private const string FailedField = "failed";
    private const string GoogleSubField = "google_sub";

    /// <summary>Returns 1 while attempts remain, 0 when the limit is reached (challenge deleted) or it is gone.</summary>
    private const string FailedAttemptScript = """
        if redis.call('EXISTS', KEYS[1]) == 0 then return 0 end
        local failed = redis.call('HINCRBY', KEYS[1], 'failed', 1)
        if failed >= tonumber(ARGV[1]) then redis.call('DEL', KEYS[1]) return 0 end
        return 1
        """;

    /// <summary>Reads and deletes in one step: only one caller gets the fields.</summary>
    private const string ConsumeScript = """
        local fields = redis.call('HGETALL', KEYS[1])
        if #fields > 0 then redis.call('DEL', KEYS[1]) end
        return fields
        """;

    public Task<string> CreateAsync(Guid platformUserId, TimeSpan lifetime, CancellationToken ct = default)
        => CreateCoreAsync(platformUserId, null, lifetime);

    public Task<string> CreateForGoogleLinkAsync(Guid platformUserId, string googleSub, TimeSpan lifetime, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(googleSub);
        return CreateCoreAsync(platformUserId, googleSub, lifetime);
    }

    private async Task<string> CreateCoreAsync(Guid platformUserId, string? googleSub, TimeSpan lifetime)
    {
        var raw = tokens.GenerateOpaqueToken();
        var expiresAt = clock.UtcNow.Add(lifetime);
        var key = Key(raw);
        var fields = new List<HashEntry>
        {
            new(UserField, platformUserId.ToString("N")),
            new(ExpiresField, expiresAt.ToUnixTimeMilliseconds()),
            new(FailedField, 0),
        };
        if (googleSub is not null)
            fields.Add(new HashEntry(GoogleSubField, googleSub));

        var db = redis.GetDatabase();
        var transaction = db.CreateTransaction();
        _ = transaction.HashSetAsync(key, fields.ToArray());
        _ = transaction.KeyExpireAsync(key, lifetime);
        await transaction.ExecuteAsync();
        return raw;
    }

    public async Task<PlatformMfaChallengeState?> GetAsync(string challenge, CancellationToken ct = default)
        => Read(await redis.GetDatabase().HashGetAllAsync(Key(challenge)));

    public async Task<bool> RegisterFailedAttemptAsync(string challenge, int maximumAttempts, CancellationToken ct = default)
    {
        var result = await redis.GetDatabase().ScriptEvaluateAsync(FailedAttemptScript, [Key(challenge)], [maximumAttempts]);
        return (long)result == 1;
    }

    public async Task<PlatformMfaChallengeState?> TryConsumeAsync(string challenge, CancellationToken ct = default)
    {
        var result = await redis.GetDatabase().ScriptEvaluateAsync(ConsumeScript, [Key(challenge)]);
        var values = (RedisValue[]?)result ?? [];
        var entries = new HashEntry[values.Length / 2];
        for (var i = 0; i < entries.Length; i++)
            entries[i] = new HashEntry(values[2 * i], values[2 * i + 1]);
        return Read(entries);
    }

    private RedisKey Key(string challenge)
        => $"{SharedStateRegistration.KeyPrefix}admin-mfa:{tokens.HashToken(challenge)}";

    private PlatformMfaChallengeState? Read(HashEntry[] entries)
    {
        if (entries.Length == 0)
            return null;
        var map = entries.ToDictionary(e => e.Name.ToString(), e => e.Value);
        if (!map.TryGetValue(UserField, out var user) || !Guid.TryParse(user.ToString(), out var userId)
            || !map.TryGetValue(ExpiresField, out var expires) || !expires.TryParse(out long expiresMs))
            return null;

        var expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(expiresMs);
        if (expiresAt <= clock.UtcNow)
            return null;
        var failed = map.TryGetValue(FailedField, out var f) && f.TryParse(out int count) ? count : 0;
        string? googleSub = map.TryGetValue(GoogleSubField, out var g) && !g.IsNullOrEmpty
            ? g.ToString()
            : null;
        return new PlatformMfaChallengeState(userId, expiresAt, failed, googleSub);
    }
}
