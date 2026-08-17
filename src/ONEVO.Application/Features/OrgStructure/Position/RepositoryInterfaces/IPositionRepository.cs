using ONEVO.Domain.Features.OrgStructure.Entities;

// Namespace deliberately stops at the feature segment: a ".Position" segment would
// collide with the Position entity type and force using-aliases everywhere (same
// convention as IDepartmentRepository/ILegalEntityRepository).
namespace ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

public interface IPositionRepository
{
    Task AddAsync(Position position, CancellationToken ct = default);

    void Update(Position position);

    Task<Position?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<Position?> GetByIdAsync(Guid tenantId, Guid positionId, CancellationToken ct = default);

    Task<Position?> GetByIdForLegalEntityAsync(Guid tenantId, Guid legalEntityId, Guid positionId, CancellationToken ct = default);

    Task<IReadOnlyList<Position>> GetByIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

    Task<IReadOnlyList<Position>> ListByLegalEntityAsync(
        Guid tenantId, Guid legalEntityId, bool includeInactive = false, Guid? departmentId = null, CancellationToken ct = default);

    Task<bool> ExistsByCodeAsync(
        Guid tenantId, Guid legalEntityId, string code, Guid? excludingPositionId = null, CancellationToken ct = default);

    Task<bool> ExistsByNameAsync(
        Guid tenantId, Guid legalEntityId, string name, Guid? excludingPositionId = null, CancellationToken ct = default);

    Task<bool> ExistsInDepartmentAsync(
        Guid tenantId, Guid legalEntityId, Guid departmentId, Guid positionId, CancellationToken ct = default);

    Task<bool> IsDescendantAsync(
        Guid tenantId, Guid legalEntityId, Guid positionId, Guid possibleDescendantId, CancellationToken ct = default);

    Task<int> CountActiveByDepartmentAsync(
        Guid tenantId, Guid legalEntityId, Guid departmentId, CancellationToken ct = default);

    Task<int> CountActiveReportsToPositionAsync(
        Guid tenantId, Guid legalEntityId, Guid positionId, CancellationToken ct = default);

    Task<PositionPage> ListPageAsync(
        Guid tenantId,
        Guid legalEntityId,
        Guid? departmentId,
        string? search,
        bool includeInactive,
        string sortBy,
        string sortDirection,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<int> CountHeadDepartmentReferencesAsync(
        Guid tenantId, Guid legalEntityId, Guid positionId, CancellationToken ct = default);

    // Ancillary reporting & coverage helpers for Position foundation
    Task AddReportingHistoryAsync(PositionReportingHistory history, CancellationToken ct = default);

    Task AddManagementCoverageRecordAsync(ManagementCoverageRecord record, CancellationToken ct = default);

    Task<PositionReportingHistory?> GetCurrentReportingHistoryAsync(
        Guid tenantId, Guid positionId, CancellationToken ct = default);

    void UpdateReportingHistory(PositionReportingHistory history);

    Task<ManagementCoverageRecord?> GetLockedReportingStructureCoverageAsync(
        Guid tenantId, Guid ownerPositionId, Guid coveredPositionId, CancellationToken ct = default);

    Task<IReadOnlyList<ManagementCoverageRecord>> ListCoverageByOwnerPositionAsync(
        Guid tenantId, Guid legalEntityId, Guid ownerPositionId, CancellationToken ct = default);

    Task<ManagementCoverageRecord?> GetCoverageRecordByIdAsync(
        Guid tenantId, Guid id, CancellationToken ct = default);

    void RemoveCoverageRecord(ManagementCoverageRecord record);

    // GetCoverageRecordByIdAsync returns a detached (AsNoTracking) entity, so a mutated OwnerOrder
    // must be pushed back through this explicit Update call - mirrors UpdateReportingHistory/
    // UpdateAccessTemplate - or SaveChangesAsync silently persists nothing.
    void UpdateCoverageRecord(ManagementCoverageRecord record);

    // Duplicate-order guard: true when an active coverage record already exists for the same
    // covered target (position/department/company) at the given responsibility order, regardless
    // of which position owns it - the uniqueness is per covered target, not per owner.
    Task<bool> HasActiveCoverageConflictAsync(
        Guid tenantId,
        Guid legalEntityId,
        string coveredTargetType,
        Guid? coveredPositionId,
        Guid? coveredDepartmentId,
        int ownerOrder,
        Guid? excludingRecordId = null,
        CancellationToken ct = default);

    // Access template helpers
    Task<PositionAccessTemplate?> GetAccessTemplateByPositionAsync(Guid tenantId, Guid positionId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, bool>> GetRequiresApprovalByPositionIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> positionIds, CancellationToken ct = default);
    Task AddAccessTemplateAsync(PositionAccessTemplate template, CancellationToken ct = default);
    void UpdateAccessTemplate(PositionAccessTemplate template);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
