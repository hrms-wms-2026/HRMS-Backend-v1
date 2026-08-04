using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;

public interface IProjectRepository
{
    Task<bool> IdentifierExistsForTenantAsync(Guid tenantId, string identifier, CancellationToken ct = default);

    Task AddAsync(Project project, CancellationToken ct = default);

    Task<Project?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    void Update(Project project);

    /// <summary>
    /// Projects where the given user has at least one active project_members row, joined and
    /// distinct on project_id (a user can be a member of the same project via more than one
    /// Objective, since project_members' uniqueness is (tenant_id, project_id, objective_id,
    /// user_id), not (tenant_id, project_id, user_id) — this must never return the same project
    /// twice). Both the project and the membership row must be active.
    /// </summary>
    Task<(IReadOnlyList<Project> Items, int TotalCount)> ListForMemberAsync(
        Guid tenantId, Guid targetUserId, int skip, int take, string? sortBy, string sortDirection,
        CancellationToken ct = default);
}
