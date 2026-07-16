using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Common.ServiceInterfaces;

public interface IModuleEntitlementService
{
    Task<bool> IsModuleEnabledAsync(
        Guid tenantId,
        string moduleKey,
        CancellationToken ct = default);

    Task<IReadOnlyList<Permission>> GetEntitledPermissionsAsync(
        IReadOnlyList<string> moduleKeys,
        CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetActiveModuleKeysForTenantAsync(
        Guid tenantId,
        CancellationToken ct = default);

    Task<IReadOnlyList<Permission>> GetAssignablePermissionsForTenantAsync(
        Guid tenantId,
        CancellationToken ct = default);

    Task<IReadOnlyList<Permission>> GetAssignablePermissionsForTenantAsync(
        Guid tenantId,
        IReadOnlyList<Guid> permissionIds,
        CancellationToken ct = default);
}
