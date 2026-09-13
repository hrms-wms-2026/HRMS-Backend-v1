using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;

public sealed class EfSessionRepository : ISessionRepository
{
    private readonly ApplicationDbContext _db;

    public EfSessionRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Session?> GetLatestActiveByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        var session = await _db.Sessions
            .Where(s => s.UserId == userId && !s.IsRevoked)
            .OrderByDescending(s => s.LastActivityAt)
            .FirstOrDefaultAsync(ct);
        return session;
    }

    public async Task<Session?> GetByIdAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await _db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        return session;
    }

    public async Task<Session?> GetByKeyHashAsync(string keyHash, CancellationToken ct = default)
    {
        var session = await _db.Sessions.FirstOrDefaultAsync(s => s.KeyHash == keyHash, ct);
        return session;
    }

    public async Task<Session?> GetByKeyHashForTenantResolutionAsync(string keyHash, CancellationToken ct = default)
    {
        // set_config(..., is_local: true) reverts automatically at transaction end, so this never
        // leaks onto the pooled physical connection for a later, unrelated caller to see.
        // CreateExecutionStrategy() wrapping is required because the runtime connection has
        // EnableRetryOnFailure configured - EF Core forbids a user-initiated BeginTransactionAsync
        // under a retrying execution strategy unless it runs inside ExecuteAsync.
        var executionStrategy = _db.Database.CreateExecutionStrategy();
        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT set_config('app.session_lookup_key_hash', {keyHash}, true)", ct);
            var session = await _db.Sessions.FirstOrDefaultAsync(s => s.KeyHash == keyHash, ct);
            await transaction.CommitAsync(ct);
            return session;
        });
    }

    public async Task RevokeByKeyHashAsync(string keyHash, CancellationToken ct = default)
    {
        var session = await _db.Sessions.FirstOrDefaultAsync(s => s.KeyHash == keyHash, ct);
        if (session is not null)
        {
            session.IsRevoked = true;
        }
        // Caller must call IUnitOfWork.SaveChangesAsync
    }

    public async Task RevokeByIdAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await _db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is not null)
        {
            session.IsRevoked = true;
        }
        // Caller must call IUnitOfWork.SaveChangesAsync
    }

    public async Task<int> RevokeAllActiveByUserIdAsync(Guid userId, CancellationToken ct = default)
        => await _db.Sessions
            .Where(s => s.UserId == userId && !s.IsRevoked)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.IsRevoked, true), ct);

    public Task AddAsync(Session session, CancellationToken ct = default)
    {
        var addTask = _db.Sessions.AddAsync(session, ct).AsTask();
        return addTask;
    }

    public async Task<IReadOnlyList<Session>> ListActiveByTenantIdAsync(
        Guid tenantId, DateTimeOffset now, CancellationToken ct = default)
    {
        var sessions = await _db.Sessions
            .Where(s => s.TenantId == tenantId && !s.IsRevoked && s.ExpiresAt > now)
            .OrderByDescending(s => s.LastActivityAt)
            .ToListAsync(ct);
        return sessions;
    }
}
