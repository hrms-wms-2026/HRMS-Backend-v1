using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;

namespace ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;

public interface IProjectMemberRepository
{
    Task AddAsync(ProjectMember member, CancellationToken ct = default);

    Task<bool> HasActiveMembershipAsync(Guid tenantId, Guid projectId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// The membership row for this exact (project, objective, user) triple, regardless of
    /// IsActive — tracked, so the caller can reactivate (IsActive=true, RemovedAt=null) or
    /// deactivate (IsActive=false, RemovedAt=now) it and rely on SaveChanges's automatic partial
    /// UPDATE. Null if no row has ever existed for this triple (a genuinely new membership).
    /// </summary>
    Task<ProjectMember?> GetTrackedForObjectiveAsync(Guid tenantId, Guid projectId, Guid objectiveId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// True if the user has any OTHER active membership row in this project (any ObjectiveId
    /// except the one excluded) — used to decide whether removing/deactivating one milestone's
    /// membership should also drop the user from the project entirely (design §3 Transfer step 6,
    /// §6 Achieve membership cleanup).
    /// </summary>
    Task<bool> HasActiveMembershipExcludingObjectiveAsync(Guid tenantId, Guid projectId, Guid userId, Guid excludingObjectiveId, CancellationToken ct = default);

    /// <summary>
    /// True if the user has an active membership row scoped to any of the given ObjectiveIds -
    /// used for the "self or any ancestor" visibility check (design §5). Callers pass the target
    /// Objective's own Id plus its full ancestor chain.
    /// </summary>
    Task<bool> HasActiveMembershipForAnyObjectiveAsync(Guid tenantId, Guid projectId, Guid userId, IReadOnlyList<Guid> objectiveIds, CancellationToken ct = default);

    /// <summary>All ObjectiveIds this user has an active membership on, within this project.</summary>
    Task<IReadOnlyList<Guid>> GetActiveObjectiveIdsForUserInProjectAsync(Guid tenantId, Guid projectId, Guid userId, CancellationToken ct = default);

    /// <summary>Every deactivated (IsActive = false, RemovedAt set) membership row for this user, across all projects in the tenant - the raw material for the "milestones I used to participate in" history view.</summary>
    Task<IReadOnlyList<ProjectMember>> ListInactiveMembershipsForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    void Update(ProjectMember member);
}
