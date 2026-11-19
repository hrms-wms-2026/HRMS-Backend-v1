using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;

public sealed class EfSessionRepository : ISessionRepository
{
    private readonly ApplicationDbContext _db;
    private readonly ISessionTicketCache? _ticketCache;

    // The cache is optional only so standalone test contexts can build the repository; the API
    // always registers it. Bulk updates bypass the EF eviction interceptor, so they evict here.
    public EfSessionRepository(ApplicationDbContext db, ISessionTicketCache? ticketCache = null)
    {
        _db = db;
        _ticketCache = ticketCache;
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
        // SESSION_CONTEXT lasts for the session, so the key is cleared in finally: it must never stay
        // on a connection for a later query to see. The transaction pins one connection, and
        // CreateExecutionStrategy() is required because EF Core forbids a user-initiated
        // BeginTransactionAsync under a retrying strategy unless it runs inside ExecuteAsync.
        var executionStrategy = _db.Database.CreateExecutionStrategy();
        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"EXEC sp_set_session_context N'app.session_lookup_key_hash', {keyHash}", ct);
                var session = await _db.Sessions.FirstOrDefaultAsync(s => s.KeyHash == keyHash, ct);
                await transaction.CommitAsync(ct);
                return session;
            }
            finally
            {
                if (_db.Database.GetDbConnection().State == System.Data.ConnectionState.Open)
                {
                    await _db.Database.ExecuteSqlRawAsync(
                        "EXEC sp_set_session_context N'app.session_lookup_key_hash', NULL", CancellationToken.None);
                }
            }
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
    {
        _ticketCache?.EvictUser(userId);
        try
        {
            return await _db.Sessions
                .Where(s => s.UserId == userId && !s.IsRevoked)
                .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.IsRevoked, true), ct);
        }
        finally
        {
            _ticketCache?.EvictUser(userId);
        }
    }

    public async Task<int> RevokeAllActiveByUserIdExceptAsync(
        Guid userId, Guid keepSessionId, CancellationToken ct = default)
    {
        _ticketCache?.EvictUser(userId);
        try
        {
            return await _db.Sessions
                .Where(s => s.UserId == userId && !s.IsRevoked && s.Id != keepSessionId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.IsRevoked, true), ct);
        }
        finally
        {
            _ticketCache?.EvictUser(userId);
        }
    }

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
