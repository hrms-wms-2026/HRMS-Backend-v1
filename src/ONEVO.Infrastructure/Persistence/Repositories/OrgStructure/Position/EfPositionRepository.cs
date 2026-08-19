using Microsoft.EntityFrameworkCore;
using Npgsql;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.OrgStructure.Entities;

// Namespace deliberately stops at the feature segment: a ".Position" segment would
// collide with the Position entity type and force using-aliases everywhere (same
// convention as EfDepartmentRepository/EfLegalEntityRepository).
namespace ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;

public class EfPositionRepository : IPositionRepository
{
    // Names of the partial unique indexes declared in ManagementCoverageRecordConfiguration that
    // enforce "one active record per covered target per responsibility order". A violation of any
    // of these under concurrent writes is translated into a CoverageOrderConflictException instead
    // of surfacing the raw Postgres unique-violation as an unhandled 500.
    private static readonly string[] CoverageOrderUniqueConstraintNames =
    [
        "ix_management_coverage_records_active_position_order",
        "ix_management_coverage_records_active_department_order",
        "ix_management_coverage_records_active_company_order"
    ];

    private readonly ApplicationDbContext _db;

    public EfPositionRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(Position position, CancellationToken ct = default)
    {
        await _db.Positions.AddAsync(position, ct);
    }

    public void Update(Position position)
    {
        _db.Positions.Update(position);
    }

    public async Task<Position?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var result = await _db.Positions
            .AsNoTracking()
            .Where(position => position.Id == id)
            .FirstOrDefaultAsync(ct);

        return result;
    }

    public async Task<Position?> GetByIdAsync(Guid tenantId, Guid positionId, CancellationToken ct = default)
    {
        var result = await _db.Positions
            .AsNoTracking()
            .Where(position => position.TenantId == tenantId && position.Id == positionId)
            .FirstOrDefaultAsync(ct);

        return result;
    }

    public async Task<IReadOnlyList<Position>> GetByIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0)
            return Array.Empty<Position>();

        return await _db.Positions
            .AsNoTracking()
            .Where(position => position.TenantId == tenantId && ids.Contains(position.Id))
            .ToListAsync(ct);
    }

    public async Task<Position?> GetByIdForLegalEntityAsync(
        Guid tenantId, Guid legalEntityId, Guid positionId, CancellationToken ct = default)
    {
        var result = await _db.Positions
            .AsNoTracking()
            .Where(position =>
                position.TenantId == tenantId
                && position.LegalEntityId == legalEntityId
                && position.Id == positionId)
            .FirstOrDefaultAsync(ct);

        return result;
    }

    public async Task<IReadOnlyList<Position>> ListByLegalEntityAsync(
        Guid tenantId, Guid legalEntityId, bool includeInactive = false, Guid? departmentId = null, CancellationToken ct = default)
    {
        var query = _db.Positions
            .AsNoTracking()
            .Where(position => position.TenantId == tenantId && position.LegalEntityId == legalEntityId);

        if (!includeInactive)
        {
            query = query.Where(position => position.IsActive);
        }

        if (departmentId is not null)
        {
            query = query.Where(position => position.DepartmentId == departmentId.Value);
        }

        query = query.OrderBy(position => position.Name).ThenBy(position => position.Id);

        var results = await query.ToListAsync(ct);
        return results;
    }

    public async Task<bool> ExistsByCodeAsync(
        Guid tenantId, Guid legalEntityId, string code, Guid? excludingPositionId = null, CancellationToken ct = default)
    {
        var normalizedCode = code.ToLower();

        var query = _db.Positions
            .AsNoTracking()
            .Where(position =>
                position.TenantId == tenantId
                && position.LegalEntityId == legalEntityId
                && position.Code != null
                && position.Code.ToLower() == normalizedCode);

        if (excludingPositionId is not null)
        {
            query = query.Where(position => position.Id != excludingPositionId.Value);
        }

        var exists = await query.AnyAsync(ct);
        return exists;
    }

    public async Task<bool> ExistsByNameAsync(
        Guid tenantId, Guid legalEntityId, string name, Guid? excludingPositionId = null, CancellationToken ct = default)
    {
        var query = _db.Positions
            .AsNoTracking()
            .Where(position =>
                position.TenantId == tenantId
                && position.LegalEntityId == legalEntityId
                && position.Name == name);

        if (excludingPositionId is not null)
        {
            query = query.Where(position => position.Id != excludingPositionId.Value);
        }

        var exists = await query.AnyAsync(ct);
        return exists;
    }

    public async Task<bool> ExistsInDepartmentAsync(
        Guid tenantId, Guid legalEntityId, Guid departmentId, Guid positionId, CancellationToken ct = default)
    {
        var exists = await _db.Positions
            .AsNoTracking()
            .Where(position =>
                position.TenantId == tenantId
                && position.LegalEntityId == legalEntityId
                && position.DepartmentId == departmentId
                && position.Id == positionId)
            .AnyAsync(ct);

        return exists;
    }

    public async Task<bool> IsDescendantAsync(
        Guid tenantId, Guid legalEntityId, Guid positionId, Guid possibleDescendantId, CancellationToken ct = default)
    {
        var descendantIds = _db.Database.SqlQuery<Guid>($@"
            WITH RECURSIVE descendants AS (
                SELECT id FROM positions
                WHERE tenant_id = {tenantId} AND legal_entity_id = {legalEntityId}
                    AND reports_to_position_id = {positionId}
                UNION ALL
                SELECT p.id FROM positions p
                INNER JOIN descendants ON p.reports_to_position_id = descendants.id
                WHERE p.tenant_id = {tenantId} AND p.legal_entity_id = {legalEntityId}
            )
            SELECT id AS ""Value"" FROM descendants
        ");

        var isDescendant = await descendantIds.AnyAsync(id => id == possibleDescendantId, ct);
        return isDescendant;
    }

    public async Task<int> CountActiveByDepartmentAsync(
        Guid tenantId, Guid legalEntityId, Guid departmentId, CancellationToken ct = default)
    {
        var count = await _db.Positions
            .AsNoTracking()
            .Where(position =>
                position.TenantId == tenantId
                && position.LegalEntityId == legalEntityId
                && position.DepartmentId == departmentId
                && position.IsActive)
            .CountAsync(ct);

        return count;
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountActiveByDepartmentIdsAsync(
        Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> departmentIds, CancellationToken ct = default)
    {
        if (departmentIds.Count == 0)
            return new Dictionary<Guid, int>();

        var counts = await _db.Positions
            .AsNoTracking()
            .Where(position =>
                position.TenantId == tenantId
                && position.LegalEntityId == legalEntityId
                && position.DepartmentId != null
                && departmentIds.Contains(position.DepartmentId!.Value)
                && position.IsActive)
            .GroupBy(position => position.DepartmentId!.Value)
            .Select(group => new { DepartmentId = group.Key, Count = group.Count() })
            .ToListAsync(ct);

        return counts.ToDictionary(row => row.DepartmentId, row => row.Count);
    }

    public async Task<int> CountActiveReportsToPositionAsync(
        Guid tenantId, Guid legalEntityId, Guid positionId, CancellationToken ct = default)
    {
        var count = await _db.Positions
            .AsNoTracking()
            .Where(position =>
                position.TenantId == tenantId
                && position.LegalEntityId == legalEntityId
                && position.ReportsToPositionId == positionId
                && position.IsActive)
            .CountAsync(ct);

        return count;
    }

    public async Task<PositionPage> ListPageAsync(
        Guid tenantId,
        Guid legalEntityId,
        Guid? departmentId,
        string? search,
        bool includeInactive,
        string sortBy,
        string sortDirection,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _db.Positions
            .AsNoTracking()
            .Where(position => position.TenantId == tenantId && position.LegalEntityId == legalEntityId);

        if (!includeInactive)
        {
            query = query.Where(position => position.IsActive);
        }

        if (departmentId is not null)
        {
            query = query.Where(position => position.DepartmentId == departmentId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLower();
            query = query.Where(position =>
                position.Name.ToLower().Contains(normalizedSearch)
                || (position.Code != null && position.Code.ToLower().Contains(normalizedSearch)));
        }

        query = ApplySort(query, sortBy, sortDirection);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        return new PositionPage(items, totalCount, page, pageSize, totalPages);
    }

    private static IQueryable<Position> ApplySort(IQueryable<Position> query, string sortBy, string sortDirection)
    {
        var ascending = sortDirection == "asc";

        return sortBy switch
        {
            "code" => ascending
                ? query.OrderBy(position => position.Code).ThenBy(position => position.Id)
                : query.OrderByDescending(position => position.Code).ThenBy(position => position.Id),
            "department" => ascending
                ? query.OrderBy(position => position.DepartmentId).ThenBy(position => position.Id)
                : query.OrderByDescending(position => position.DepartmentId).ThenBy(position => position.Id),
            "reportsto" => ascending
                ? query.OrderBy(position => position.ReportsToPositionId).ThenBy(position => position.Id)
                : query.OrderByDescending(position => position.ReportsToPositionId).ThenBy(position => position.Id),
            "type" => ascending
                ? query.OrderBy(position => position.PositionType).ThenBy(position => position.Id)
                : query.OrderByDescending(position => position.PositionType).ThenBy(position => position.Id),
            "capacity" => ascending
                ? query.OrderBy(position => position.MaxOccupancy).ThenBy(position => position.Id)
                : query.OrderByDescending(position => position.MaxOccupancy).ThenBy(position => position.Id),
            "status" => ascending
                ? query.OrderBy(position => position.IsActive).ThenBy(position => position.Id)
                : query.OrderByDescending(position => position.IsActive).ThenBy(position => position.Id),
            "createdat" => ascending
                ? query.OrderBy(position => position.CreatedAt).ThenBy(position => position.Id)
                : query.OrderByDescending(position => position.CreatedAt).ThenBy(position => position.Id),
            "updatedat" => ascending
                ? query.OrderBy(position => position.UpdatedAt).ThenBy(position => position.Id)
                : query.OrderByDescending(position => position.UpdatedAt).ThenBy(position => position.Id),
            _ => ascending
                ? query.OrderBy(position => position.Name).ThenBy(position => position.Id)
                : query.OrderByDescending(position => position.Name).ThenBy(position => position.Id),
        };
    }

    public async Task<int> CountHeadDepartmentReferencesAsync(
        Guid tenantId, Guid legalEntityId, Guid positionId, CancellationToken ct = default)
    {
        var count = await _db.Departments
            .AsNoTracking()
            .Where(department =>
                department.TenantId == tenantId
                && department.LegalEntityId == legalEntityId
                && department.HeadPositionId == positionId)
            .CountAsync(ct);

        return count;
    }

    public async Task AddReportingHistoryAsync(PositionReportingHistory history, CancellationToken ct = default)
    {
        await _db.PositionReportingHistories.AddAsync(history, ct);
    }

    public async Task AddManagementCoverageRecordAsync(ManagementCoverageRecord record, CancellationToken ct = default)
    {
        await _db.ManagementCoverageRecords.AddAsync(record, ct);
    }

    public async Task<PositionReportingHistory?> GetCurrentReportingHistoryAsync(
        Guid tenantId, Guid positionId, CancellationToken ct = default)
    {
        var result = await _db.PositionReportingHistories
            .AsNoTracking()
            .Where(h => h.TenantId == tenantId && h.PositionId == positionId && h.EffectiveTo == null)
            .OrderByDescending(h => h.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

        return result;
    }

    public void UpdateReportingHistory(PositionReportingHistory history)
    {
        _db.PositionReportingHistories.Update(history);
    }

    public async Task<ManagementCoverageRecord?> GetLockedReportingStructureCoverageAsync(
        Guid tenantId, Guid ownerPositionId, Guid coveredPositionId, CancellationToken ct = default)
    {
        var result = await _db.ManagementCoverageRecords
            .AsNoTracking()
            .Where(m =>
                m.TenantId == tenantId
                && m.OwnerPositionId == ownerPositionId
                && m.CoveredTargetType == ManagementCoverageRecord.TargetPosition
                && m.CoveredPositionId == coveredPositionId
                && m.Source == ManagementCoverageRecord.SourceReportingStructure
                && m.IsLocked
                && m.Status == ManagementCoverageRecord.StatusActive)
            .FirstOrDefaultAsync(ct);

        return result;
    }

    public async Task<IReadOnlyList<ManagementCoverageRecord>> ListCoverageByOwnerPositionAsync(
        Guid tenantId, Guid legalEntityId, Guid ownerPositionId, CancellationToken ct = default)
    {
        var results = await _db.ManagementCoverageRecords
            .AsNoTracking()
            .Where(m =>
                m.TenantId == tenantId
                && m.LegalEntityId == legalEntityId
                && m.OwnerPositionId == ownerPositionId)
            .OrderBy(m => m.OwnerOrder)
            .ThenBy(m => m.Id)
            .ToListAsync(ct);

        return results;
    }

    public async Task<IReadOnlyList<ManagementCoverageRecord>> ListActiveCoverageByCoveredTargetAsync(
        Guid tenantId,
        Guid legalEntityId,
        string coveredTargetType,
        Guid? coveredPositionId,
        Guid? coveredDepartmentId,
        Guid? excludingRecordId,
        CancellationToken ct = default)
    {
        var query = _db.ManagementCoverageRecords
            .AsNoTracking()
            .Where(m =>
                m.TenantId == tenantId
                && m.LegalEntityId == legalEntityId
                && m.CoveredTargetType == coveredTargetType
                && m.CoveredPositionId == coveredPositionId
                && m.CoveredDepartmentId == coveredDepartmentId
                && m.Status == ManagementCoverageRecord.StatusActive);

        if (excludingRecordId is { } excludeId)
            query = query.Where(m => m.Id != excludeId);

        return await query
            .OrderBy(m => m.OwnerOrder)
            .ThenBy(m => m.Id)
            .ToListAsync(ct);
    }

    public async Task<ManagementCoverageRecord?> GetCoverageRecordByIdAsync(
        Guid tenantId, Guid id, CancellationToken ct = default)
    {
        var result = await _db.ManagementCoverageRecords
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.Id == id)
            .FirstOrDefaultAsync(ct);

        return result;
    }

    public void RemoveCoverageRecord(ManagementCoverageRecord record)
    {
        _db.ManagementCoverageRecords.Remove(record);
    }

    public void UpdateCoverageRecord(ManagementCoverageRecord record)
    {
        _db.ManagementCoverageRecords.Update(record);
    }

    public async Task<bool> HasActiveCoverageConflictAsync(
        Guid tenantId,
        Guid legalEntityId,
        string coveredTargetType,
        Guid? coveredPositionId,
        Guid? coveredDepartmentId,
        int ownerOrder,
        Guid? excludingRecordId = null,
        CancellationToken ct = default)
    {
        var query = _db.ManagementCoverageRecords
            .AsNoTracking()
            .Where(m =>
                m.TenantId == tenantId
                && m.LegalEntityId == legalEntityId
                && m.CoveredTargetType == coveredTargetType
                && m.CoveredPositionId == coveredPositionId
                && m.CoveredDepartmentId == coveredDepartmentId
                && m.OwnerOrder == ownerOrder
                && m.Status == ManagementCoverageRecord.StatusActive);

        if (excludingRecordId is { } excludeId)
        {
            query = query.Where(m => m.Id != excludeId);
        }

        var exists = await query.AnyAsync(ct);
        return exists;
    }

    public async Task<IReadOnlyList<ManagementCoverageRecord>> ListActiveCoverageByCoveredTargetAsync(
        Guid tenantId,
        Guid legalEntityId,
        string coveredTargetType,
        Guid? coveredPositionId,
        Guid? coveredDepartmentId,
        Guid? excludingRecordId = null,
        CancellationToken ct = default)
    {
        var query = _db.ManagementCoverageRecords
            .AsNoTracking()
            .Where(m =>
                m.TenantId == tenantId
                && m.LegalEntityId == legalEntityId
                && m.CoveredTargetType == coveredTargetType
                && m.CoveredPositionId == coveredPositionId
                && m.CoveredDepartmentId == coveredDepartmentId
                && m.Status == ManagementCoverageRecord.StatusActive);

        if (excludingRecordId is { } excludeId)
        {
            query = query.Where(m => m.Id != excludeId);
        }

        var results = await query.OrderBy(m => m.OwnerOrder).ToListAsync(ct);
        return results;
    }

    public async Task<PositionAccessTemplate?> GetAccessTemplateByPositionAsync(
        Guid tenantId, Guid positionId, CancellationToken ct = default)
    {
        var result = await _db.Set<PositionAccessTemplate>()
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.PositionId == positionId && t.IsActive)
            .FirstOrDefaultAsync(ct);

        return result;
    }

    public async Task<PositionAccessTemplate?> GetAccessTemplateByPositionIncludingInactiveAsync(
        Guid tenantId, Guid positionId, CancellationToken ct = default)
    {
        var result = await _db.Set<PositionAccessTemplate>()
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.PositionId == positionId)
            .FirstOrDefaultAsync(ct);

        return result;
    }

    public async Task<IReadOnlyDictionary<Guid, bool>> GetRequiresApprovalByPositionIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> positionIds, CancellationToken ct = default)
    {
        if (positionIds.Count == 0)
            return new Dictionary<Guid, bool>();

        return await _db.Set<PositionAccessTemplate>()
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId && positionIds.Contains(t.PositionId) && t.IsActive)
            .ToDictionaryAsync(t => t.PositionId, t => t.RequiresApproval, ct);
    }

    public async Task AddAccessTemplateAsync(PositionAccessTemplate template, CancellationToken ct = default)
    {
        await _db.Set<PositionAccessTemplate>().AddAsync(template, ct);
    }

    public void UpdateAccessTemplate(PositionAccessTemplate template)
    {
        _db.Set<PositionAccessTemplate>().Update(template);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            var affectedRows = await _db.SaveChangesAsync(ct);
            return affectedRows;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        } pgEx && CoverageOrderUniqueConstraintNames.Contains(pgEx.ConstraintName))
        {
            throw new CoverageOrderConflictException(
                "An active coverage record already exists for this covered target at this responsibility level.",
                ex);
        }
    }
}
