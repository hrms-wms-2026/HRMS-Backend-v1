using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;

public interface ISprintRepository
{
    Task AddAsync(Sprint sprint, CancellationToken ct = default);
    Task<Sprint?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<Sprint?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Sprint>> GetByObjectiveIdAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default);
    Task<IReadOnlyList<Sprint>> GetByProjectAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);

    /// <summary>Active sprints for one Objective - what non-owner members see in Backlog (spec permissions table).</summary>
    Task<IReadOnlyList<Sprint>> GetActiveByObjectiveIdAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default);

    /// <summary>Tenant-unscoped, for SprintLifecycleJob's periodic sweep across every tenant.</summary>
    Task<IReadOnlyList<Sprint>> GetByStatusAsync(string status, CancellationToken ct = default);

    void Update(Sprint sprint);
}
