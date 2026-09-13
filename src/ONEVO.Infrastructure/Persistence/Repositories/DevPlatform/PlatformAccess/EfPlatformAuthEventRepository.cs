using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.PlatformAccess;

public sealed class EfPlatformAuthEventRepository : IPlatformAuthEventRepository
{
    private readonly ApplicationDbContext _db;

    public EfPlatformAuthEventRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<PlatformAuthEvent>> ListByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        await _db.PlatformAuthEvents.AsNoTracking().Where(e => e.UserId == userId).OrderByDescending(e => e.CreatedAt).ToListAsync(ct);

    public async Task<IReadOnlyList<PlatformAuthEvent>> ListAllAsync(CancellationToken ct = default) =>
        await _db.PlatformAuthEvents.AsNoTracking().OrderByDescending(e => e.CreatedAt).ToListAsync(ct);

    public Task AddAsync(PlatformAuthEvent authEvent, CancellationToken ct = default) =>
        _db.PlatformAuthEvents.AddAsync(authEvent, ct).AsTask();
}
