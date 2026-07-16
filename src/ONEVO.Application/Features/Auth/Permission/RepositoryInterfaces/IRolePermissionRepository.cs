using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;

public interface IRolePermissionRepository
{
    Task<IReadOnlyList<RolePermission>> ListByRoleAsync(Guid roleId, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<RolePermission> rolePermissions, CancellationToken ct = default);
    void RemoveRange(IEnumerable<RolePermission> rolePermissions);
}
