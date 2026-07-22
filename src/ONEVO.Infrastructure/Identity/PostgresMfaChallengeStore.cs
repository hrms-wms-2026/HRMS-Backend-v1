using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Identity;

/// <summary>
/// Normal runtime implementation of <see cref="IMfaChallengeStore"/>, backed by the canonical
/// Phase 1 mfa_challenges table. Only the SHA-256 hash of the opaque challenge is persisted; the
/// raw value is returned once to the caller so it can be placed in the HttpOnly onevo_mfa cookie
/// and is never stored or logged. Consumption is single-use: consumed_at is an EF concurrency
/// token, so two racing verifications cannot both consume the same challenge. Failed attempts are
/// registered with a single atomic UPDATE so parallel invalid attempts cannot lose increments and
/// the lockout threshold is enforced at the database level.
/// </summary>
public sealed class PostgresMfaChallengeStore : IMfaChallengeStore
{
    private readonly ApplicationDbContext _db;
    private readonly ISecureTokenGenerator _tokens;
    private readonly IDateTimeProvider _clock;

    public PostgresMfaChallengeStore(
        ApplicationDbContext db,
        ISecureTokenGenerator tokens,
        IDateTimeProvider clock)
    {
        _db = db;
        _tokens = tokens;
        _clock = clock;
    }

    public async Task<string> CreateAsync(
        Guid userId,
        Guid tenantId,
        TimeSpan lifetime,
        CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        // Opportunistic cleanup: expired or already-consumed challenges for this user are dead
        // rows and can be removed before issuing a new challenge.
        var staleChallenges = await _db.MfaChallenges
            .Where(c => c.TenantId == tenantId
                && c.UserId == userId
                && (c.ExpiresAt <= now || c.ConsumedAt != null))
            .ToListAsync(ct);
        if (staleChallenges.Count > 0)
        {
            _db.MfaChallenges.RemoveRange(staleChallenges);
        }

        var rawChallenge = _tokens.GenerateOpaqueToken();

        var challenge = new MfaChallenge
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            ChallengeHash = _tokens.HashToken(rawChallenge),
            FailedAttemptCount = 0,
            ExpiresAt = now.Add(lifetime),
            ConsumedAt = null,
            CreatedAt = now
        };

        _db.MfaChallenges.Add(challenge);
        await _db.SaveChangesAsync(ct);

        return rawChallenge;
    }

    public async Task<MfaChallengeState?> GetAsync(
        string challenge,
        CancellationToken ct = default)
    {
        var row = await FindActiveRowAsync(challenge, ct);
        if (row is null)
            return null;

        return ToState(row);
    }

    public async Task<bool> RegisterFailedAttemptAsync(
        string challenge,
        int maximumAttempts,
        CancellationToken ct = default)
    {
        var challengeHash = _tokens.HashToken(challenge);
        var now = _clock.UtcNow;

        // Single atomic UPDATE: the database increments failed_attempt_count from its current
        // stored value and, in the same statement, sets consumed_at when the new count reaches
        // the maximum. Both SetProperty value expressions read the pre-update column value, so
        // parallel invalid attempts each add exactly one and no increment can be lost, unlike a
        // load-increment-save round trip.
        var updatedRows = await _db.MfaChallenges
            .Where(c => c.ChallengeHash == challengeHash)
            .Where(c => c.ConsumedAt == null)
            .Where(c => c.ExpiresAt > now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        c => c.FailedAttemptCount,
                        c => c.FailedAttemptCount + 1)
                    .SetProperty(
                        c => c.ConsumedAt,
                        c => c.FailedAttemptCount + 1 >= maximumAttempts
                            ? (DateTimeOffset?)now
                            : c.ConsumedAt),
                ct);

        // No active row matched: unknown hash, expired, already consumed, or a concurrent
        // operation invalidated the challenge first.
        if (updatedRows == 0)
            return false;

        // ExecuteUpdateAsync bypasses the change tracker; detach any stale tracked copy of this
        // row so later reads in this context observe the updated column values.
        DetachTrackedChallenge(challengeHash);

        // The challenge "remains active" only if the row is still unconsumed after our increment.
        // This read cannot report a stale true: if the update above locked the challenge out, or
        // a concurrent operation consumed it in the meantime, consumed_at is already set and we
        // return false.
        var updatedRow = await _db.MfaChallenges
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ChallengeHash == challengeHash, ct);

        if (updatedRow is null)
            return false;

        if (updatedRow.ConsumedAt is not null)
            return false;

        if (updatedRow.ExpiresAt <= now)
            return false;

        return true;
    }

    public async Task<MfaChallengeState?> TryConsumeAsync(
        string challenge,
        CancellationToken ct = default)
    {
        var row = await FindActiveRowAsync(challenge, ct);
        if (row is null)
            return null;

        var state = ToState(row);
        row.ConsumedAt = _clock.UtcNow;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent request consumed this challenge first; it is single-use.
            return null;
        }

        return state;
    }

    private void DetachTrackedChallenge(string challengeHash)
    {
        foreach (var entry in _db.ChangeTracker.Entries<MfaChallenge>())
        {
            if (entry.Entity.ChallengeHash == challengeHash)
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    private async Task<MfaChallenge?> FindActiveRowAsync(string challenge, CancellationToken ct)
    {
        var challengeHash = _tokens.HashToken(challenge);
        var now = _clock.UtcNow;

        var row = await _db.MfaChallenges
            .FirstOrDefaultAsync(c => c.ChallengeHash == challengeHash, ct);

        if (row is null)
            return null;

        if (row.ConsumedAt is not null)
            return null;

        if (row.ExpiresAt <= now)
            return null;

        return row;
    }

    private static MfaChallengeState ToState(MfaChallenge row)
    {
        return new MfaChallengeState(
            row.UserId,
            row.TenantId,
            row.ExpiresAt,
            row.FailedAttemptCount);
    }
}
