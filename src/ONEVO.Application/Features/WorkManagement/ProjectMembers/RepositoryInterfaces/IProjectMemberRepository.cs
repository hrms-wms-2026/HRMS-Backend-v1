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

    /// <summary>
    /// Every project_members row for this exact (project, user) pair, regardless of IsActive -
    /// unlike GetActiveObjectiveIdsForUserInProjectAsync (active-only, Guid list) this returns the
    /// full rows (including IsActive/RemovedAt) for every status, so a caller can show "all
    /// milestones I've ever been connected to in this project" and let the frontend filter by
    /// status instead of the API pre-filtering.
    /// </summary>
    Task<IReadOnlyList<ProjectMember>> ListForUserInProjectAsync(Guid tenantId, Guid projectId, Guid userId, CancellationToken ct = default);

    void Update(ProjectMember member);

    /// <summary>Batched, per-project, deduplicated-by-user list of active member user ids (a user with multiple objective memberships in the same project counts once), capped at takePerProject, earliest joiners first. For rendering a capped avatar stack on a project card. Projects with no active members are absent from the result.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> ListDistinctActiveMemberUserIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> projectIds, int takePerProject, CancellationToken ct = default);

    /// <summary>Batched, per-project count of distinct active member users (not membership rows) — the "+N" overflow number to pair with ListDistinctActiveMemberUserIdsAsync. Projects with no active members are absent from the result.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountDistinctActiveMembersAsync(
        Guid tenantId, IReadOnlyCollection<Guid> projectIds, CancellationToken ct = default);
}
