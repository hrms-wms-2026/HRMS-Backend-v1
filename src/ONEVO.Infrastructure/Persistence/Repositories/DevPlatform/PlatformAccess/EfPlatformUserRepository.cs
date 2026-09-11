using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.PlatformAccess;

public sealed class EfPlatformUserRepository : IPlatformUserRepository
{
    private readonly ApplicationDbContext _db;

    public EfPlatformUserRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<PlatformUser?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.PlatformUsers.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<PlatformUser?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        _db.PlatformUsers.FirstOrDefaultAsync(u => u.Email == email, ct);

    public Task<PlatformUser?> GetByGoogleSubAsync(string googleSub, CancellationToken ct = default) =>
        _db.PlatformUsers.FirstOrDefaultAsync(u => u.GoogleSub == googleSub, ct);

    public async Task<IReadOnlyList<PlatformUser>> ListUsersAsync(CancellationToken ct = default) =>
        await _db.PlatformUsers.AsNoTracking().ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, string>> GetFirstRoleNamesByUserIdsAsync(
        IEnumerable<Guid> userIds, CancellationToken ct = default)
    {
        var ids = userIds.ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, string>();

        var assignments = await _db.PlatformUserRoles
            .Where(ur => ids.Contains(ur.UserId))
            .Join(_db.PlatformRoles, ur => ur.RoleId, r => r.Id,
                (ur, r) => new { ur.UserId, ur.AssignedAt, RoleName = r.Name })
            .ToListAsync(ct);

        return assignments
            .GroupBy(a => a.UserId)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.AssignedAt).First().RoleName);
    }

    public Task AddAsync(PlatformUser user, CancellationToken ct = default) =>
        _db.PlatformUsers.AddAsync(user, ct).AsTask();

    public void UpdateUser(PlatformUser user) =>
        _db.PlatformUsers.Update(user);

    public async Task ReplaceRolesAsync(Guid userId, IEnumerable<Guid> roleIds, CancellationToken ct = default)
    {
        var currentRoles = await _db.PlatformUserRoles.Where(ur => ur.UserId == userId).ToListAsync(ct);
        _db.PlatformUserRoles.RemoveRange(currentRoles);
        var newRoles = roleIds.Select(rId => new PlatformUserRole { UserId = userId, RoleId = rId });
        await _db.PlatformUserRoles.AddRangeAsync(newRoles, ct);
    }
}
