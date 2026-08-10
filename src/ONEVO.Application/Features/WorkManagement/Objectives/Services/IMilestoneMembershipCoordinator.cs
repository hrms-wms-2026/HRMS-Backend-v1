using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Services;

/// <summary>
/// Encapsulates the membership-lifecycle rules from
/// docs/superpowers/specs/2026-08-06-work-management-milestone-membership-and-achieve-design.md
/// §3, shared across Create/Transfer/Achieve/member-management. Never calls SaveChangesAsync -
/// callers wrap the whole operation in IUnitOfWork.ExecuteInTransactionAsync.
/// </summary>
public interface IMilestoneMembershipCoordinator
{
    /// <summary>Null if the user has no Employee record in this tenant, or their EmploymentStatusId isn't Active.</summary>
    Task<Employee?> GetActiveAssigneeAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    /// <summary>Creates a new milestone-scoped membership, or reactivates an existing inactive one. No-op if already active.</summary>
    Task UpsertMembershipAsync(Guid tenantId, Guid projectId, Guid objectiveId, Guid userId, Guid employeeId, CancellationToken ct = default);

    /// <summary>Deactivates the membership for this exact (project, objective, user) triple. No-op if no row exists.</summary>
    Task DeactivateMembershipAsync(Guid tenantId, Guid projectId, Guid objectiveId, Guid userId, CancellationToken ct = default);

    /// <summary>True if the user has any other active membership in this project (direct or a different milestone).</summary>
    Task<bool> HasOtherActiveAccessAsync(Guid tenantId, Guid projectId, Guid userId, Guid excludingObjectiveId, CancellationToken ct = default);
}
