using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;

public interface IPlatformUserRepository
{
    Task<IReadOnlyList<PlatformUser>> ListUsersAsync(CancellationToken ct = default);
    Task<PlatformUser?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PlatformUser?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<PlatformUser?> GetByGoogleSubAsync(string googleSub, CancellationToken ct = default);
    Task AddAsync(PlatformUser user, CancellationToken ct = default);
    void UpdateUser(PlatformUser user);
    Task ReplaceRolesAsync(Guid userId, IEnumerable<Guid> roleIds, CancellationToken ct = default);
}
