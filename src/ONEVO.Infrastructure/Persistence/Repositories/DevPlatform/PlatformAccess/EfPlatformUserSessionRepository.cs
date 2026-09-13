using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.PlatformAccess;

public sealed class EfPlatformUserSessionRepository : IPlatformUserSessionRepository
{
    private readonly ApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public EfPlatformUserSessionRepository(ApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public Task<PlatformUserSession?> GetByIdAsync(Guid sessionId, CancellationToken ct = default) =>
        _db.PlatformUserSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);

    public Task<PlatformUserSession?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
        _db.PlatformUserSessions.FirstOrDefaultAsync(s => s.TokenHash == tokenHash, ct);

    public Task AddAsync(PlatformUserSession session, CancellationToken ct = default) =>
        _db.PlatformUserSessions.AddAsync(session, ct).AsTask();

    public async Task RevokeByIdAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await _db.PlatformUserSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is not null && session.RevokedAt is null)
            session.RevokedAt = _clock.UtcNow;
    }

    public async Task RevokeByTokenHashAsync(string tokenHash, CancellationToken ct = default)
    {
        var session = await _db.PlatformUserSessions.FirstOrDefaultAsync(s => s.TokenHash == tokenHash, ct);
        if (session is not null && session.RevokedAt is null)
            session.RevokedAt = _clock.UtcNow;
    }

    public async Task RevokeAllByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        var sessions = await _db.PlatformUserSessions.Where(s => s.AccountId == userId && s.RevokedAt == null).ToListAsync(ct);
        foreach (var session in sessions)
        {
            session.RevokedAt = _clock.UtcNow;
        }
    }

    public async Task<IReadOnlyList<PlatformUserSession>> ListByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        await _db.PlatformUserSessions.AsNoTracking().Where(s => s.AccountId == userId).OrderByDescending(s => s.CreatedAt).ToListAsync(ct);
}
