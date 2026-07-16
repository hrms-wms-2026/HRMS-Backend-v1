using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;

public interface IPlatformRoleRepository
{
    Task<IReadOnlyList<PlatformRole>> ListRolesAsync(CancellationToken ct = default);
    Task<PlatformRole?> GetRoleByIdAsync(Guid roleId, CancellationToken ct = default);
    void UpdateRole(PlatformRole role);
    Task ReplacePermissionsAsync(Guid roleId, IEnumerable<string> permissions, CancellationToken ct = default);
}
