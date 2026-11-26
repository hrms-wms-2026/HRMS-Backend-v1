using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;

public interface IUserRoleRepository
{
    Task<IReadOnlyList<UserRole>> ListActiveByUserIdAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> ListUserIdsByRoleAsync(
        Guid roleId,
        DateTimeOffset now,
        CancellationToken ct = default);

    /// <summary>True when the user already has a row for this role (any expiry) for a role that is not deleted: user_roles is keyed on user + role.</summary>
    Task<bool> ExistsAsync(Guid userId, Guid roleId, CancellationToken ct = default);

    Task AddAsync(UserRole userRole, CancellationToken ct = default);
}
