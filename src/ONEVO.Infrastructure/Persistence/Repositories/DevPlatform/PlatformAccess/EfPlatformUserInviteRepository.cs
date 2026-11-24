using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.PlatformAccess;

public sealed class EfPlatformUserInviteRepository : IPlatformUserInviteRepository
{
    private readonly ApplicationDbContext _db;

    public EfPlatformUserInviteRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task AddAsync(PlatformUserInvite invite, CancellationToken ct = default) =>
        _db.PlatformUserInvites.AddAsync(invite, ct).AsTask();

    public Task<PlatformUserInvite?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
        _db.PlatformUserInvites.FirstOrDefaultAsync(i => i.InviteTokenHash == tokenHash, ct);

    // A re-invited user has one invite row per invite, so the latest one is the live invite.
    public Task<PlatformUserInvite?> GetByPlatformUserIdAsync(Guid platformUserId, CancellationToken ct = default) =>
        _db.PlatformUserInvites
            .Where(i => i.PlatformUserId == platformUserId)
            .OrderByDescending(i => i.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public void Update(PlatformUserInvite invite) =>
        _db.PlatformUserInvites.Update(invite);
}
