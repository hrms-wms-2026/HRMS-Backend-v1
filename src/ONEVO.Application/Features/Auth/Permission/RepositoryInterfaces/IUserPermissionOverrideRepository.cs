using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;

public interface IUserPermissionOverrideRepository
{
    Task<IReadOnlyList<UserPermissionOverrideGrant>> ListForUserAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default);

    Task AddAsync(UserPermissionOverride grant, CancellationToken ct = default);
}
