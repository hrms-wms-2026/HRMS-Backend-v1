using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;

namespace ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;

public interface IProjectMemberRepository
{
    Task AddAsync(ProjectMember member, CancellationToken ct = default);

    Task<bool> HasActiveMembershipAsync(Guid tenantId, Guid projectId, Guid employeeId, CancellationToken ct = default);

    /// <summary>Tracked - see original doc comment on the equivalent UserId-keyed method this replaces (design intent unchanged, only the identity column changed).</summary>
    Task<ProjectMember?> GetTrackedForObjectiveAsync(Guid tenantId, Guid projectId, Guid objectiveId, Guid employeeId, CancellationToken ct = default);

    Task<bool> HasActiveMembershipExcludingObjectiveAsync(Guid tenantId, Guid projectId, Guid employeeId, Guid excludingObjectiveId, CancellationToken ct = default);

    Task<bool> HasActiveMembershipForAnyObjectiveAsync(Guid tenantId, Guid projectId, Guid employeeId, IReadOnlyList<Guid> objectiveIds, CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> GetActiveObjectiveIdsForEmployeeInProjectAsync(Guid tenantId, Guid projectId, Guid employeeId, CancellationToken ct = default);

    Task<IReadOnlyList<ProjectMember>> ListInactiveMembershipsForEmployeeAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);

    Task<IReadOnlyList<ProjectMember>> ListForEmployeeInProjectAsync(Guid tenantId, Guid projectId, Guid employeeId, CancellationToken ct = default);

    /// <summary>Every active project_members row scoped to this exact objective.</summary>
    Task<IReadOnlyList<ProjectMember>> ListActiveForObjectiveAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default);

    void Update(ProjectMember member);

    /// <summary>Batched, per-project, deduplicated-by-employee list of active member employee ids, capped at takePerProject, earliest joiners first.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> ListDistinctActiveMemberEmployeeIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> projectIds, int takePerProject, CancellationToken ct = default);

    /// <summary>Batched, per-project count of distinct active member employees.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountDistinctActiveMembersAsync(
        Guid tenantId, IReadOnlyCollection<Guid> projectIds, CancellationToken ct = default);
}
