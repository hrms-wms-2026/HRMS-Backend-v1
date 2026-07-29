using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;

public sealed class EfTenantSessionExchangeChallengeRepository : ITenantSessionExchangeChallengeRepository
{
    private readonly ApplicationDbContext _db;

    public EfTenantSessionExchangeChallengeRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(TenantSessionExchangeChallenge challenge, CancellationToken ct = default)
    {
        _db.TenantSessionExchangeChallenges.Add(challenge);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<TenantSessionExchangeChallengeState?> TryConsumeAsync(
        string codeHash, Guid tenantId, DateTimeOffset now, CancellationToken ct = default)
    {
        // Single guarded UPDATE: the WHERE clause re-checks consumed_at IS NULL and expires_at at
        // the database level, so two concurrent consume attempts can never both succeed - there is
        // no read-then-update race here, unlike loading a tracked entity first.
        var updatedRows = await _db.TenantSessionExchangeChallenges
            .Where(c => c.CodeHash == codeHash)
            .Where(c => c.TenantId == tenantId)
            .Where(c => c.ConsumedAt == null)
            .Where(c => c.ExpiresAt > now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(c => c.ConsumedAt, now)
                    .SetProperty(c => c.UpdatedAt, now),
                ct);

        if (updatedRows == 0)
            return null;

        var row = await _db.TenantSessionExchangeChallenges
            .AsNoTracking()
            .Where(c => c.CodeHash == codeHash)
            .Where(c => c.TenantId == tenantId)
            .FirstOrDefaultAsync(ct);

        return row is null ? null : new TenantSessionExchangeChallengeState(row.TenantId, row.UserId, row.AuthOrigin);
    }

    public async Task<int> CleanupExpiredAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        return await _db.TenantSessionExchangeChallenges
            .Where(c => c.ExpiresAt <= now || c.ConsumedAt != null)
            .ExecuteDeleteAsync(ct);
    }
}
