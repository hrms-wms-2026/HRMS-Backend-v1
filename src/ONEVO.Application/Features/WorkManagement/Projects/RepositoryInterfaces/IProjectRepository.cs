using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;

public interface IProjectRepository
{
    Task<bool> IdentifierExistsForTenantAsync(Guid tenantId, string identifier, CancellationToken ct = default);

    Task AddAsync(Project project, CancellationToken ct = default);

    Task<Project?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>
    /// Same lookup as <see cref="GetByIdForTenantAsync"/>, but returns the entity tracked by the
    /// DbContext's change tracker instead of AsNoTracking. Use this only on write paths that
    /// mutate a subset of the entity's fields and then rely on EF's automatic change detection
    /// (SaveChanges) to produce a partial UPDATE covering just the changed columns - do NOT call
    /// <see cref="Update"/> on an entity fetched this way, since Update() unconditionally marks
    /// every property Modified regardless of tracking state, which defeats the point. Read-only
    /// callers (e.g. GetProjectByIdQueryHandler) should keep using the no-tracking
    /// GetByIdForTenantAsync for performance.
    /// </summary>
    Task<Project?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    void Update(Project project);

    /// <summary>
    /// Projects where the given employee has at least one active project_members row, joined and
    /// distinct on project_id (an employee can be a member of the same project via more than one
    /// Objective, since project_members' uniqueness is (tenant_id, project_id, objective_id,
    /// employee_id), not (tenant_id, project_id, employee_id) — this must never return the same
    /// project twice). Both the project and the membership row must be active.
    /// </summary>
    Task<(IReadOnlyList<Project> Items, int TotalCount)> ListForMemberAsync(
        Guid tenantId, Guid targetEmployeeId, int skip, int take, string? sortBy, string sortDirection,
        CancellationToken ct = default);

    /// <summary>
    /// Atomically increments projects.next_task_number and returns the value to stamp on the new
    /// task's ShortId (the pre-increment number). Uses a single UPDATE ... RETURNING so concurrent
    /// task creates cannot collide.
    /// </summary>
    Task<long> IncrementAndGetNextTaskNumberAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);
}
