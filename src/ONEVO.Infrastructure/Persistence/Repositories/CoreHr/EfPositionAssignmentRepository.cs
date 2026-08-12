using Microsoft.EntityFrameworkCore;
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
