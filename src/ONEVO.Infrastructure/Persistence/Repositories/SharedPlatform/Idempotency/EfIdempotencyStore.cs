using Microsoft.EntityFrameworkCore;
using Npgsql;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Repositories.SharedPlatform.Idempotency;

public sealed class EfIdempotencyStore : IIdempotencyStore
{
    private readonly ApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public EfIdempotencyStore(ApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<IdempotencyBeginResult> TryBeginAsync(
        string key,
        string scope,
        string requesterId,
        string requestHash,
        TimeSpan ttl,
        CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        var record = new IdempotencyRecord
        {
            Id = Guid.NewGuid(),
            Key = key,
            Scope = scope,
            RequesterId = requesterId,
            RequestHash = requestHash,
            Status = IdempotencyRecordStatus.InProgress,
            CreatedAt = now,
            ExpiresAt = now.Add(ttl)
        };

        _db.Set<IdempotencyRecord>().Add(record);
        try
        {
            // Committed immediately (not with the business transaction) so a
            // concurrent duplicate hits the unique constraint below.
            await _db.SaveChangesAsync(ct);
            return new IdempotencyBeginResult(IdempotencyOutcome.Started, record.Id);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Detach the failed insert so it does not poison the shared DbContext
            // used by the action's own unit of work afterwards.
            _db.Entry(record).State = EntityState.Detached;
        }

        var existing = await _db.Set<IdempotencyRecord>()
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Key == key && r.Scope == scope && r.RequesterId == requesterId, ct);

        if (existing is null)
        {
            // Row vanished between conflict and lookup (expired purge); treat as in flight.
            return new IdempotencyBeginResult(IdempotencyOutcome.InFlight, Guid.Empty);
        }

        if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
            return new IdempotencyBeginResult(IdempotencyOutcome.HashMismatch, existing.Id);

        if (existing.Status == IdempotencyRecordStatus.Completed)
            return new IdempotencyBeginResult(
                IdempotencyOutcome.Replay, existing.Id, existing.ResponseStatusCode, existing.ResponseBody);

        return new IdempotencyBeginResult(IdempotencyOutcome.InFlight, existing.Id);
    }

    public async Task CompleteAsync(Guid recordId, int statusCode, string? responseBody, CancellationToken ct = default)
    {
        await _db.Set<IdempotencyRecord>()
            .Where(r => r.Id == recordId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, IdempotencyRecordStatus.Completed)
                .SetProperty(r => r.ResponseStatusCode, statusCode)
                .SetProperty(r => r.ResponseBody, responseBody)
                .SetProperty(r => r.CompletedAt, _clock.UtcNow), ct);
    }

    public async Task AbandonAsync(Guid recordId, CancellationToken ct = default)
    {
        await _db.Set<IdempotencyRecord>()
            .Where(r => r.Id == recordId)
            .ExecuteDeleteAsync(ct);
    }

    public async Task<int> PurgeExpiredAsync(CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        return await _db.Set<IdempotencyRecord>()
            .Where(r => r.ExpiresAt <= now)
            .ExecuteDeleteAsync(ct);
    }
}
