using Microsoft.EntityFrameworkCore;
using Npgsql;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Features.CoreHr.PositionAssignment.Models;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.CoreHr;

public class EfPositionAssignmentRepository : IPositionAssignmentRepository
{
    private readonly ApplicationDbContext _db;

    public EfPositionAssignmentRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment?> GetActivePrimaryAsync(
        Guid tenantId, Guid employeeId, CancellationToken ct = default)
    {
        return await _db.PositionAssignments
            .AsNoTracking()
            .FirstOrDefaultAsync(pa =>
                pa.TenantId == tenantId
                && pa.EmployeeId == employeeId
                && pa.AssignmentKind == PositionAssignmentKind.PrimaryEmployment
                && pa.AssignmentStatus == PositionAssignmentStatus.Active, ct);
    }

    public async Task<int> CountActiveAsync(Guid tenantId, Guid positionId, CancellationToken ct = default)
    {
        return await _db.PositionAssignments
            .AsNoTracking()
            .CountAsync(pa =>
                pa.TenantId == tenantId
                && pa.PositionId == positionId
                && pa.AssignmentKind == PositionAssignmentKind.PrimaryEmployment
                && pa.AssignmentStatus == PositionAssignmentStatus.Active, ct);
    }

    // Single round trip: fetch every active-primary assignment row (joined to its employee) for
    // the given positions, then group and cap to previewLimit per position in memory - avoids
    // both N+1 (one call, not one per position) and a per-group SQL TOP/window translation that
    // isn't reliably portable across EF providers.
    public async Task<IReadOnlyDictionary<Guid, PositionOccupancyPreview>> GetOccupancyPreviewsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> positionIds, int previewLimit, CancellationToken ct = default)
    {
        if (positionIds.Count == 0)
            return new Dictionary<Guid, PositionOccupancyPreview>();

        var rows = await (
            from pa in _db.PositionAssignments.AsNoTracking()
            join e in _db.Employees.AsNoTracking() on pa.EmployeeId equals e.Id
            where pa.TenantId == tenantId
                && e.TenantId == tenantId
                && positionIds.Contains(pa.PositionId)
                && pa.AssignmentKind == PositionAssignmentKind.PrimaryEmployment
                && pa.AssignmentStatus == PositionAssignmentStatus.Active
            orderby pa.PositionId, pa.EffectiveFrom, pa.Id
            select new
            {
                pa.PositionId,
                EmployeeId = e.Id,
                e.FirstName,
                e.LastName,
                e.AvatarFileId
            }).ToListAsync(ct);

        return rows
            .GroupBy(row => row.PositionId)
            .ToDictionary(
                group => group.Key,
                group => new PositionOccupancyPreview(
                    group.Count(),
                    group.Take(previewLimit)
                        .Select(row => new PositionOccupantPreviewItem(row.EmployeeId, row.FirstName, row.LastName, row.AvatarFileId))
                        .ToList()));
    }

    public async Task<bool> HasActivePrimaryInLegalEntityAsync(
        Guid tenantId, Guid employeeId, Guid legalEntityId, CancellationToken ct = default)
    {
        return await (
            from pa in _db.PositionAssignments.AsNoTracking()
            join p in _db.Positions.AsNoTracking() on pa.PositionId equals p.Id
            where pa.TenantId == tenantId
                && pa.EmployeeId == employeeId
                && pa.AssignmentKind == PositionAssignmentKind.PrimaryEmployment
                && pa.AssignmentStatus == PositionAssignmentStatus.Active
                && p.LegalEntityId == legalEntityId
            select pa.Id).AnyAsync(ct);
    }

    public async Task<Guid?> TryReservePositionAssignmentAsync(
        Guid tenantId, Guid employeeId, Guid positionId, DateOnly effectiveFrom, Guid createdById,
        CancellationToken ct = default)
    {
        var newId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var rowsAffected = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO position_assignments
                (id, tenant_id, employee_id, position_id, assignment_kind, effective_from,
                 assignment_status, created_by_id, created_at, is_deleted)
            SELECT {newId}, {tenantId}, {employeeId}, {positionId}, {PositionAssignmentKind.PrimaryEmployment},
                   {effectiveFrom}, {PositionAssignmentStatus.Planned}, {createdById}, {now}, false
            WHERE (
                SELECT COUNT(*) FROM position_assignments
                WHERE tenant_id = {tenantId} AND position_id = {positionId}
                  AND assignment_kind = {PositionAssignmentKind.PrimaryEmployment}
                  AND assignment_status IN ({PositionAssignmentStatus.Active}, {PositionAssignmentStatus.Planned})
            ) < (
                SELECT max_occupancy FROM positions WHERE id = {positionId} AND tenant_id = {tenantId}
            )
        ", ct);

        return rowsAffected > 0 ? newId : null;
    }

    public async Task<bool> ActivatePlannedAsync(Guid tenantId, Guid positionAssignmentId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var rowsAffected = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE position_assignments
            SET assignment_status = {PositionAssignmentStatus.Active}, updated_at = {now}
            WHERE id = {positionAssignmentId} AND tenant_id = {tenantId}
              AND assignment_status = {PositionAssignmentStatus.Planned}
        ", ct);
        return rowsAffected > 0;
    }

    public async Task<bool> CancelPlannedAsync(Guid tenantId, Guid positionAssignmentId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var rowsAffected = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE position_assignments
            SET assignment_status = {PositionAssignmentStatus.Cancelled}, updated_at = {now}
            WHERE id = {positionAssignmentId} AND tenant_id = {tenantId}
              AND assignment_status = {PositionAssignmentStatus.Planned}
        ", ct);
        return rowsAffected > 0;
    }

    public async Task<Guid?> TryCreateActiveAssignmentAsync(
        Guid tenantId, Guid employeeId, Guid positionId, DateOnly effectiveFrom, Guid createdById,
        CancellationToken ct = default)
    {
        var newId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        try
        {
            // Concurrent change-position can still race the end-then-create sequence and hit
            // ix_position_assignments_one_active_primary_per_employee; map that to an
            // application-layer conflict instead of an unhandled 500.
            var rowsAffected = await _db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO position_assignments
                    (id, tenant_id, employee_id, position_id, assignment_kind, effective_from,
                     assignment_status, created_by_id, created_at, is_deleted)
                SELECT {newId}, {tenantId}, {employeeId}, {positionId}, {PositionAssignmentKind.PrimaryEmployment},
                       {effectiveFrom}, {PositionAssignmentStatus.Active}, {createdById}, {now}, false
                WHERE (
                    SELECT COUNT(*) FROM position_assignments
                    WHERE tenant_id = {tenantId} AND position_id = {positionId}
                      AND assignment_kind = {PositionAssignmentKind.PrimaryEmployment}
                      AND assignment_status IN ({PositionAssignmentStatus.Active}, {PositionAssignmentStatus.Planned})
                ) < (
                    SELECT max_occupancy FROM positions WHERE id = {positionId} AND tenant_id = {tenantId}
                )
            ", ct);

            return rowsAffected > 0 ? newId : null;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new UniqueConstraintConflictException(ex);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // ExecuteSqlInterpolatedAsync typically surfaces Npgsql's PostgresException directly
            // (unlike SaveChanges, which wraps it in DbUpdateException).
            throw new UniqueConstraintConflictException(ex);
        }
    }

    public async Task<bool> EndActiveAsync(Guid tenantId, Guid positionAssignmentId, DateOnly effectiveTo, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var rowsAffected = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE position_assignments
            SET assignment_status = {PositionAssignmentStatus.Ended}, effective_to = {effectiveTo}, updated_at = {now}
            WHERE id = {positionAssignmentId} AND tenant_id = {tenantId}
              AND assignment_status = {PositionAssignmentStatus.Active}
        ", ct);
        return rowsAffected > 0;
    }

    public async Task AddAsync(ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment assignment, CancellationToken ct = default)
    {
        await _db.PositionAssignments.AddAsync(assignment, ct);
    }

    public async Task<ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment?> GetTrackedAsync(
        Guid tenantId, Guid id, CancellationToken ct = default)
    {
        return await _db.PositionAssignments
            .FirstOrDefaultAsync(pa => pa.TenantId == tenantId && pa.Id == id, ct);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return await _db.SaveChangesAsync(ct);
    }
}
