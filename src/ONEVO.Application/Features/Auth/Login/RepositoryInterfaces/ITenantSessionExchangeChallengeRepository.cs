using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;

/// <summary>Narrow, immutable projection returned by a successful consume - never the tracked entity.</summary>
public sealed record TenantSessionExchangeChallengeState(Guid TenantId, Guid UserId, string AuthOrigin);

public interface ITenantSessionExchangeChallengeRepository
{
    Task AddAsync(TenantSessionExchangeChallenge challenge, CancellationToken ct = default);

    /// <summary>
    /// Atomically consumes the challenge matching <paramref name="codeHash"/> and
    /// <paramref name="tenantId"/>: a single guarded UPDATE, not a read-then-update round trip.
    /// Returns null if no row matches (unknown hash, wrong tenant, already consumed, or expired) -
    /// callers must treat all of those identically (generic 401), never revealing which condition
    /// failed.
    /// </summary>
    Task<TenantSessionExchangeChallengeState?> TryConsumeAsync(
        string codeHash, Guid tenantId, DateTimeOffset now, CancellationToken ct = default);

    Task<int> CleanupExpiredAsync(DateTimeOffset now, CancellationToken ct = default);
}
