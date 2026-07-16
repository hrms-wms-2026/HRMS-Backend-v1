using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;

public interface IRoleRepository
{
    Task<Role?> GetRoleByIdAsync(Guid roleId, CancellationToken ct = default);
    Task<Role?> GetByIdForTenantAsync(Guid tenantId, Guid roleId, CancellationToken ct = default);
    Task<Role?> GetByNameForTenantAsync(Guid tenantId, string name, CancellationToken ct = default);
    Task<Role?> GetBySourceTemplateForTenantAsync(Guid tenantId, Guid templateId, CancellationToken ct = default);
    Task<IReadOnlyList<Role>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(Role role, CancellationToken ct = default);
    void Remove(Role role);
}
