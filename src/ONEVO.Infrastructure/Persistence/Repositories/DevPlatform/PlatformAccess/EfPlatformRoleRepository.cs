using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.PlatformAccess;

public sealed class EfPlatformRoleRepository : IPlatformRoleRepository
{
    private readonly ApplicationDbContext _db;

    public EfPlatformRoleRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<PlatformRole>> ListRolesAsync(CancellationToken ct = default) =>
        await _db.PlatformRoles.AsNoTracking().ToListAsync(ct);

    public Task<PlatformRole?> GetRoleByIdAsync(Guid roleId, CancellationToken ct = default) =>
        _db.PlatformRoles.FirstOrDefaultAsync(r => r.Id == roleId, ct);

    public void UpdateRole(PlatformRole role) =>
        _db.PlatformRoles.Update(role);

    public async Task ReplacePermissionsAsync(Guid roleId, IEnumerable<string> permissions, CancellationToken ct = default)
    {
        var currentPerms = await _db.PlatformRolePermissions.Where(rp => rp.RoleId == roleId).ToListAsync(ct);
        _db.PlatformRolePermissions.RemoveRange(currentPerms);
        var newPerms = permissions.Select(p => new PlatformRolePermission { RoleId = roleId, PermissionCode = p });
        await _db.PlatformRolePermissions.AddRangeAsync(newPerms, ct);
    }
}
