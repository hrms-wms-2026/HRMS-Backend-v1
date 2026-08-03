using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;

public interface IProjectRepository
{
    Task<bool> IdentifierExistsForTenantAsync(Guid tenantId, string identifier, CancellationToken ct = default);

    Task AddAsync(Project project, CancellationToken ct = default);
}
