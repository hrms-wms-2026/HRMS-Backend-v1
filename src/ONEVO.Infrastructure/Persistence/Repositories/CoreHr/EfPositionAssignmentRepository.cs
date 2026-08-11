using Microsoft.EntityFrameworkCore;
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
                && pa.AssignmentStatus == PositionAssignmentStatus.Active, ct);
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
