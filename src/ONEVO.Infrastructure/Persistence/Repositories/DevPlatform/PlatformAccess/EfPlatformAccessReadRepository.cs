using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.PlatformAccess;

/// <summary>
/// Read access for platform RBAC resolution:
/// platform_user_roles -> platform_roles -> platform_role_permissions.
/// </summary>
public sealed class EfPlatformAccessReadRepository : IPlatformAccessReadRepository
{
    private readonly ApplicationDbContext _db;

    public EfPlatformAccessReadRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<List<PlatformUserRole>> GetUserRolesAsync(Guid userId, CancellationToken ct = default) =>
        _db.PlatformUserRoles.AsNoTracking().Where(ur => ur.UserId == userId).ToListAsync(ct);

    public Task<List<PlatformRole>> GetRolesByIdsAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken ct = default) =>
        _db.PlatformRoles.AsNoTracking().Where(r => roleIds.Contains(r.Id)).ToListAsync(ct);

    public Task<List<PlatformRolePermission>> GetRolePermissionsAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken ct = default) =>
        _db.PlatformRolePermissions.AsNoTracking().Where(rp => roleIds.Contains(rp.RoleId)).ToListAsync(ct);
}
