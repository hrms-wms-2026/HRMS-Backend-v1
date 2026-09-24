using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;

public interface ISprintRepository
{
    Task AddAsync(Sprint sprint, CancellationToken ct = default);
    Task<Sprint?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<Sprint?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Sprint>> GetByProjectAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);

    /// <summary>Sprints holding at least one task of this Module (spec §3.3 - the Tree tab's leaf expansion).</summary>
    Task<IReadOnlyList<Sprint>> GetContainingObjectiveTasksAsync(Guid tenantId, Guid objectiveId, bool activeOnly, CancellationToken ct = default);

    /// <summary>Whether any task of this Module currently sits in an Active sprint - the Achieve-Module gate
    /// (sprints are project-level now, so this replaces the old Sprint.ObjectiveId-based active-sprint check).</summary>
    Task<bool> AnyActiveContainingObjectiveTasksAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default);

    /// <summary>Tenant-unscoped, for SprintLifecycleJob's periodic sweep across every tenant.</summary>
    Task<IReadOnlyList<Sprint>> GetByStatusAsync(string status, CancellationToken ct = default);

    void Update(Sprint sprint);
}
