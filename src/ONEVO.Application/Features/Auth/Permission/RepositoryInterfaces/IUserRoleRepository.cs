using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;

public interface IUserRoleRepository
{
    Task<IReadOnlyList<UserRole>> ListActiveByUserIdAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> ListUserIdsByRoleAsync(Guid roleId, CancellationToken ct = default);

    Task AddAsync(UserRole userRole, CancellationToken ct = default);
}
