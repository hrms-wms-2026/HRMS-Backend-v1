using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;

namespace ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;

public interface IProjectMemberInvitationRepository
{
    Task AddAsync(ProjectMemberInvitation invitation, CancellationToken ct = default);

    Task<ProjectMemberInvitation?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>Tracked variant of <see cref="GetByIdForTenantAsync"/> for accept/reject mutation.</summary>
    Task<ProjectMemberInvitation?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>The single pending invitation for this exact (objective, employee) pair, if any — used by Add Member's duplicate check and Remove Member's cancel branch.</summary>
    Task<ProjectMemberInvitation?> GetPendingForObjectiveAndEmployeeAsync(Guid tenantId, Guid objectiveId, Guid employeeId, CancellationToken ct = default);

    /// <summary>Tracked variant of <see cref="GetPendingForObjectiveAndEmployeeAsync"/>, for Remove Member's cancel mutation.</summary>
    Task<ProjectMemberInvitation?> GetTrackedPendingForObjectiveAndEmployeeAsync(Guid tenantId, Guid objectiveId, Guid employeeId, CancellationToken ct = default);

    /// <summary>Every pending invitation for this objective — the "Request pending" rows merged into Get Objective Members.</summary>
    Task<IReadOnlyList<ProjectMemberInvitation>> ListPendingForObjectiveAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default);

    /// <summary>Every pending invitation addressed to this employee, across all objectives — backs My Objective Invitations.</summary>
    Task<IReadOnlyList<ProjectMemberInvitation>> ListPendingForEmployeeAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);

    void Update(ProjectMemberInvitation invitation);
}
