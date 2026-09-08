using ONEVO.Application.Features.CoreHr.PositionAssignment.Models;

namespace ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;

public interface IPositionAssignmentRepository
{
    Task<ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment?> GetActivePrimaryAsync(
        Guid tenantId, Guid employeeId, CancellationToken ct = default);

    // Position capacity signal for max_occupancy enforcement (FinalizeOnboardingDraftCommandHandler,
    // ApproveAccessGrantRequestCommandHandler): counts active PrimaryEmployment assignments only.
    // AdditionalAuthority does not consume a seat - positions are the seat/headcount model
    // (phase1-table-inventory.md: "First-class org seats"), only PrimaryEmployment is
    // structurally seat-constrained (the partial unique index enforcing at most one active
    // Primary Employment assignment per employee has no AdditionalAuthority equivalent), and
    // GetActivePrimaryAsync/HasActivePrimaryInLegalEntityAsync already define "is this employee
    // seated" the same way. Deliberately the same rule GetOccupancyPreviewsAsync uses below, so
    // assignedCount from the occupant preview always equals what capacity enforcement allows.
    Task<int> CountActiveAsync(Guid tenantId, Guid positionId, CancellationToken ct = default);

    // Batches the occupant-preview data for every position in positionIds in a single query:
    // active PrimaryEmployment assignments only (matches GetActivePrimaryAsync's kind filter -
    // AdditionalAuthority holders are not "occupants" for seat-preview purposes), grouped and
    // capped to previewLimit per position in memory. A position with no active primary
    // assignments is simply absent from the returned dictionary.
    Task<IReadOnlyDictionary<Guid, PositionOccupancyPreview>> GetOccupancyPreviewsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> positionIds, int previewLimit, CancellationToken ct = default);

    Task<bool> HasActivePrimaryInLegalEntityAsync(
        Guid tenantId, Guid employeeId, Guid legalEntityId, CancellationToken ct = default);

    /// <summary>Atomically reserves a seat for the given position by inserting a "planned"
    /// PositionAssignment row, guarded by a capacity subquery in the same SQL statement (counts
    /// both active and planned occupants against Position.MaxOccupancy). Returns the new row's
    /// Id on success, or null if the position was already at capacity - no separate count-then-
    /// insert round trip, so two concurrent callers targeting the last vacancy cannot both
    /// succeed.</summary>
    Task<Guid?> TryReservePositionAssignmentAsync(
        Guid tenantId,
        Guid employeeId,
        Guid positionId,
        DateOnly effectiveFrom,
        Guid createdById,
        Guid? reportsToEmployeeId,
        CancellationToken ct = default);

    /// <summary>Flips a "planned" PositionAssignment row to "active" (on invite accept). No-op
    /// (returns false) if the row doesn't exist or isn't currently "planned".</summary>
    Task<bool> ActivatePlannedAsync(Guid tenantId, Guid positionAssignmentId, CancellationToken ct = default);

    /// <summary>Flips a "planned" PositionAssignment row to "cancelled" (on invite revoke),
    /// freeing the seat. No-op (returns false) if the row doesn't exist or isn't currently
    /// "planned".</summary>
    Task<bool> CancelPlannedAsync(Guid tenantId, Guid positionAssignmentId, CancellationToken ct = default);

    /// <summary>Same atomic capacity-guarded INSERT as TryReservePositionAssignmentAsync, but
    /// inserts the row as "active" directly - used for immediate, non-invitation position
    /// changes (Change Position action) rather than an invitation's reserve-then-activate
    /// lifecycle.</summary>
    Task<Guid?> TryCreateActiveAssignmentAsync(
        Guid tenantId, Guid employeeId, Guid positionId, DateOnly effectiveFrom, Guid createdById,
        Guid? reportsToEmployeeId, CancellationToken ct = default);

    /// <summary>Current active PrimaryEmployment holders of a position, with work email — used to
    /// disambiguate a reporting-manager override (onboarding wizard picker, bulk-onboarding CSV
    /// email match, Change Position picker) against who is actually eligible right now.</summary>
    Task<IReadOnlyList<PositionActiveHolder>> GetActiveHoldersAsync(
        Guid tenantId, Guid positionId, CancellationToken ct = default);

    /// <summary>Batched GetActivePrimaryAsync: current active PrimaryEmployment assignment per
    /// employee id, keyed by EmployeeId. Ids with no active primary assignment are absent from
    /// the result - the ToDictionary(EmployeeId) keying is safe because the query's filter
    /// (AssignmentKind == PrimaryEmployment, AssignmentStatus == Active) exactly matches the
    /// unique index ix_position_assignments_one_active_primary_per_employee (unique on
    /// EmployeeId, filtered to assignment_kind = 'PrimaryEmployment' AND assignment_status =
    /// 'active'), so at most one row per EmployeeId can match. Same invariant
    /// GetActivePrimaryAsync itself relies on for its single-row FirstOrDefault.</summary>
    Task<IReadOnlyDictionary<Guid, ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment>> GetActivePrimaryByEmployeeIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default);

    /// <summary>Batched GetActiveHoldersAsync: current active PrimaryEmployment holders per owner
    /// position id, keyed by PositionId. Same join shape as GetOccupancyPreviewsAsync above.
    /// Position ids with no active holders are absent from the result.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<PositionActiveHolder>>> GetActiveHoldersByPositionIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> positionIds, CancellationToken ct = default);

    /// <summary>Active PrimaryEmployment holders who are themselves active employees with a
    /// user account. Used to pick a concrete checklist assignee (UserId) during onboarding.</summary>
    Task<IReadOnlyList<ChecklistAssignee>> GetChecklistAssigneesAsync(
        Guid tenantId, Guid positionId, CancellationToken ct = default);

    Task<bool> EndActiveAsync(Guid tenantId, Guid positionAssignmentId, DateOnly effectiveTo, CancellationToken ct = default);

    Task AddAsync(ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment assignment, CancellationToken ct = default);

    Task<ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment?> GetTrackedAsync(
        Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>PrimaryEmployment assignments in Active or Ended status for the employee,
    /// oldest EffectiveFrom first. Planned (and Cancelled) rows are not history.</summary>
    Task<IReadOnlyList<ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment>> ListHistoryForEmployeeAsync(
        Guid tenantId, Guid employeeId, CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
