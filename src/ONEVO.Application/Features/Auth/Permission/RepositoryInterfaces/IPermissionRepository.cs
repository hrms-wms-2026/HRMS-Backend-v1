using ONEVO.Domain.Features.Auth.Entities;
using AuthPermission = ONEVO.Domain.Features.Auth.Entities.Permission;

namespace ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;

public interface IPermissionRepository
{
    Task<AuthPermission?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<IReadOnlyList<AuthPermission>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
    Task<IReadOnlyList<AuthPermission>> GetByCodesAsync(IEnumerable<string> codes, CancellationToken ct = default);
    Task<bool> UserHasPermissionCodeAsync(
        Guid userId,
        string permissionCode,
        DateTimeOffset now,
        CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListRolePermissionCodesAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct = default);
    Task<IReadOnlyList<PermissionCodeWithModule>> ListRolePermissionCodesWithModulesAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct = default);
}
