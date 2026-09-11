using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;

public sealed class EfRolePermissionRepository : IRolePermissionRepository
{
    private readonly ApplicationDbContext _db;

    public EfRolePermissionRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<RolePermission>> ListByRoleAsync(Guid roleId, CancellationToken ct = default)
    {
        var rolePermissions = await _db.RolePermissions
            .Where(rp => rp.RoleId == roleId)
            .ToListAsync(ct);
        return rolePermissions;
    }

    public Task AddRangeAsync(IEnumerable<RolePermission> rolePermissions, CancellationToken ct = default)
    {
        var addRangeTask = _db.RolePermissions.AddRangeAsync(rolePermissions, ct);
        return addRangeTask;
    }

    public void RemoveRange(IEnumerable<RolePermission> rolePermissions)
    {
        _db.RolePermissions.RemoveRange(rolePermissions);
    }
}
