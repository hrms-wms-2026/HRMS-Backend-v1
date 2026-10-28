using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Services.SharedPlatform.Outbox;

/// <summary>
/// The database operations that give one processor instance exclusive ownership of an outbox
/// message. Delivery guarantee: at-least-once with no concurrent double-processing. It is NOT
/// exactly-once: claiming stops two instances from dispatching the same message at the same time,
/// but a crash after a provider accepted a send and before CompleteAsync commits leaves the row
/// Processing, so it is reclaimed and retried once the lease expires, which may deliver a duplicate
/// (unless the provider honours the idempotency key, see OutboxDispatchContext).
///
/// State machine (owner = locked_by):
///   pending    --claim-->            processing(owner, locked_until = now() + lease)
///   processing --lease expired-->    claimable again by any instance (crash recovery); the reclaim
///                                    counts the abandoned attempt if its handler had started, and
///                                    at MaxAttempts the new owner parks the message as failed
///                                    instead of dispatching it
///   processing --complete(owner)-->  published
///   processing --fail(owner)-->      pending (backoff) | failed (after MaxAttempts)
///   processing --release(owner)-->   pending (graceful shutdown; attempt not counted)
/// Every transition out of processing is guarded by id + locked_by + status, so an instance whose
/// claim was taken over can never overwrite the new owner's outcome. Lease times use the database
/// clock (now()) so instances with skewed clocks still agree on when a lease has expired.
///
/// outbox_messages is platform-scoped and has no RLS policy, so these statements behave the same
/// for onevo_app (NOBYPASSRLS) whatever tenant context the connection is in, exactly like the
/// unclaimed SELECT the processor used before.
/// </summary>
public static class OutboxClaimStore
{
    /// <summary>
    /// Atomically claims up to <paramref name="batchSize"/> messages that are due (pending and
    /// next_attempt_at reached) or whose previous owner's lease has expired. FOR UPDATE SKIP LOCKED
    /// makes concurrent claimers skip rows another claimer is taking, instead of blocking on them
    /// or taking them twice; each row is returned to exactly one claimer.
    /// Reclaiming an expired lease counts the abandoned attempt (attempt_count + 1, last_error
    /// says why) when its handler had started - RenewLeaseAsync stamps attempt_started_at right
    /// before the handler runs, and a claim clears it - so OutboxRetryPolicy's max-attempts rule
    /// bounds messages whose attempts never finish, while a message that only waited behind slow
    /// work in the previous owner's batch is reclaimed without losing an attempt.
    /// SET expressions read the row's old values, so the CASEs see the old status.
    /// </summary>
    public static async Task<List<OutboxMessage>> ClaimBatchAsync(
        ApplicationDbContext db, string owner, TimeSpan lease, int batchSize, CancellationToken ct)
    {
        var claimed = await db.Set<OutboxMessage>()
            .FromSqlInterpolated($"""
                WITH claimable AS MATERIALIZED (
                    SELECT id
                    FROM outbox_messages
                    WHERE (status = {OutboxMessageStatus.Pending} AND next_attempt_at <= now())
                       OR (status = {OutboxMessageStatus.Processing} AND locked_until < now())
                    ORDER BY next_attempt_at, created_at
                    LIMIT {batchSize}
                    FOR UPDATE SKIP LOCKED
                )
                UPDATE outbox_messages AS m
                SET status = {OutboxMessageStatus.Processing},
                    attempt_count = CASE WHEN m.status = {OutboxMessageStatus.Processing} AND m.attempt_started_at IS NOT NULL
                                         THEN m.attempt_count + 1 ELSE m.attempt_count END,
                    last_error = CASE WHEN m.status = {OutboxMessageStatus.Processing} AND m.attempt_started_at IS NOT NULL
                                      THEN 'Attempt by ' || coalesce(m.locked_by, 'unknown instance') || ' did not finish before its lease expired; reclaimed.'
                                      ELSE m.last_error END,
                    locked_by = {owner},
                    locked_until = now() + {lease},
                    attempt_started_at = NULL
                FROM claimable
                WHERE m.id = claimable.id
                RETURNING m.*
                """)
            .AsNoTracking()
            .ToListAsync(ct);

        return claimed.OrderBy(m => m.NextAttemptAt).ThenBy(m => m.CreatedAt).ToList();
    }

    /// <summary>
    /// Restarts the owner's lease right before a message's handler runs, so the lease only has to
    /// cover one handler run, not the wait behind earlier messages of the same batch. Returns false
    /// when another instance has reclaimed the message meanwhile; the caller must then skip it.
    /// </summary>
    public static async Task<bool> RenewLeaseAsync(
        ApplicationDbContext db, Guid id, string owner, TimeSpan lease, CancellationToken ct)
    {
        var updated = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE outbox_messages
            SET locked_until = now() + {lease}, attempt_started_at = now()
            WHERE id = {id} AND locked_by = {owner} AND status = {OutboxMessageStatus.Processing}
            """, ct);
        return updated == 1;
    }

    /// <summary>Marks the message Published if, and only if, <paramref name="owner"/> still owns it.</summary>
    public static async Task<bool> CompleteAsync(
        ApplicationDbContext db, Guid id, string owner, DateTimeOffset processedAt, CancellationToken ct)
    {
        var updated = await Owned(db, id, owner).ExecuteUpdateAsync(s => s
            .SetProperty(m => m.Status, OutboxMessageStatus.Published)
            .SetProperty(m => m.ProcessedAt, processedAt)
            .SetProperty(m => m.LastError, (string?)null)
            .SetProperty(m => m.LockedBy, (string?)null)
            .SetProperty(m => m.LockedUntil, (DateTimeOffset?)null), ct);
        return updated == 1;
    }

    /// <summary>Applies a failed attempt's outcome (back to Pending with backoff, or Failed) if, and
    /// only if, <paramref name="owner"/> still owns the message.</summary>
    public static async Task<bool> FailAsync(
        ApplicationDbContext db, Guid id, string owner, OutboxFailureOutcome outcome, CancellationToken ct)
    {
        var updated = await Owned(db, id, owner).ExecuteUpdateAsync(s =>
        {
            s.SetProperty(m => m.Status, outcome.Status)
             .SetProperty(m => m.AttemptCount, outcome.AttemptCount)
             .SetProperty(m => m.LastError, outcome.LastError)
             .SetProperty(m => m.ProcessedAt, outcome.ProcessedAt)
             .SetProperty(m => m.LockedBy, (string?)null)
             .SetProperty(m => m.LockedUntil, (DateTimeOffset?)null);
            if (outcome.NextAttemptAt is { } next)
                s.SetProperty(m => m.NextAttemptAt, next);
        }, ct);
        return updated == 1;
    }

    /// <summary>Hands claimed messages back as Pending without counting an attempt (graceful
    /// shutdown), only where <paramref name="owner"/> still owns them.</summary>
    public static Task<int> ReleaseAsync(
        ApplicationDbContext db, IReadOnlyCollection<Guid> ids, string owner, CancellationToken ct)
        => db.Set<OutboxMessage>()
            .Where(m => ids.Contains(m.Id) && m.LockedBy == owner && m.Status == OutboxMessageStatus.Processing)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, OutboxMessageStatus.Pending)
                .SetProperty(m => m.LockedBy, (string?)null)
                .SetProperty(m => m.LockedUntil, (DateTimeOffset?)null), ct);

    private static IQueryable<OutboxMessage> Owned(ApplicationDbContext db, Guid id, string owner)
        => db.Set<OutboxMessage>()
            .Where(m => m.Id == id && m.LockedBy == owner && m.Status == OutboxMessageStatus.Processing);
}
