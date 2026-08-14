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

    Task AddAsync(ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment assignment, CancellationToken ct = default);

    Task<ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment?> GetTrackedAsync(
        Guid tenantId, Guid id, CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
