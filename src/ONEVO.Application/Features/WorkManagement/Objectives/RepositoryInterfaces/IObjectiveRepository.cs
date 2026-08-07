using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;

public interface IObjectiveRepository
{
    Task AddAsync(Objective objective, CancellationToken ct = default);

    Task<Objective?> GetDefaultByProjectIdAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);

    /// <summary>
    /// Same lookup as <see cref="GetDefaultByProjectIdAsync"/>, but returns the entity tracked by
    /// the DbContext's change tracker instead of AsNoTracking. Use this only on write paths that
    /// mutate a subset of fields and rely on EF's automatic change detection (SaveChanges) for a
    /// partial UPDATE - do NOT call <see cref="Update"/> afterward, since Update() unconditionally
    /// marks every property Modified regardless of tracking state.
    /// </summary>
    Task<Objective?> GetTrackedDefaultByProjectIdAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);

    Task<Objective?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>Every Objective for a Project, unordered - the caller builds the tree from ParentObjectiveId.</summary>
    Task<IReadOnlyList<Objective>> GetTreeByProjectIdAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);

    /// <summary>Every Objective for a Project regardless of IsActive, unordered - used to build a
    /// Head-scoped subtree in memory. Unlike GetTreeByProjectIdAsync, does not filter to active-only.</summary>
    Task<IReadOnlyList<Objective>> GetAllByProjectIdAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);

    void Update(Objective objective);
}
