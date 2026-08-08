using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;

public interface IProjectCategoryRepository
{
    Task<ProjectCategory?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ProjectCategory>> GetAllForTenantAsync(Guid tenantId, bool includeInactive = false, CancellationToken ct = default);
}
