using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Services;

/// <summary>
/// Encapsulates the membership-lifecycle rules from
/// docs/superpowers/specs/2026-08-06-work-management-milestone-membership-and-achieve-design.md
/// §3, shared across Create/Transfer/Achieve/member-management. Never calls SaveChangesAsync -
/// callers wrap the whole operation in IUnitOfWork.ExecuteInTransactionAsync. EmployeeId-keyed
/// throughout (Phase 2, 2026-08-14) - callers resolve the caller's own EmployeeId via
/// ICallerIdentityResolver before calling in here; a target person's EmployeeId (e.g. the invitee
/// being added) already flows in from the wire as EmployeeId directly.
/// </summary>
public interface IMilestoneMembershipCoordinator
{
    /// <summary>Null if no active Employee exists with this Id in this tenant, or their EmploymentStatusId isn't Active.</summary>
    Task<Employee?> GetActiveAssigneeAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);

    /// <summary>Creates a new milestone-scoped membership, or reactivates an existing inactive one. No-op if already active.</summary>
    Task UpsertMembershipAsync(Guid tenantId, Guid projectId, Guid objectiveId, Guid employeeId, CancellationToken ct = default);

    /// <summary>Deactivates the membership for this exact (project, objective, employee) triple. No-op if no row exists.</summary>
    Task DeactivateMembershipAsync(Guid tenantId, Guid projectId, Guid objectiveId, Guid employeeId, CancellationToken ct = default);

    /// <summary>True if the employee has any other active membership in this project (direct or a different milestone).</summary>
    Task<bool> HasOtherActiveAccessAsync(Guid tenantId, Guid projectId, Guid employeeId, Guid excludingObjectiveId, CancellationToken ct = default);

    /// <summary>True if the employee has an active membership row scoped to exactly this objective.</summary>
    Task<bool> HasActiveMembershipAsync(Guid tenantId, Guid projectId, Guid objectiveId, Guid employeeId, CancellationToken ct = default);

    /// <summary>True if the employee has an active membership on this objective (looks up by objective id only).</summary>
    Task<bool> IsActiveMemberAsync(Guid tenantId, Guid objectiveId, Guid employeeId, CancellationToken ct = default);
}
