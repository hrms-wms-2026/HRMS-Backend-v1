namespace ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;

public interface IUserPermissionOverrideRepository
{
    Task<IReadOnlyList<UserPermissionOverrideGrant>> ListForUserAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default);
}
