using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories;

public class EfEmployeeRepository : IEmployeeRepository
{
    private readonly ApplicationDbContext _db;

    public EfEmployeeRepository(ApplicationDbContext db) => _db = db;

    public async Task<Employee?> GetByUserIdAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        return await _db.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.UserId == userId, ct);
    }

    public async Task<IReadOnlyList<Employee>> GetByUserIdsAsync(Guid tenantId, IReadOnlyList<Guid> userIds, CancellationToken ct = default)
    {
        return await _db.Employees
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && userIds.Contains(e.UserId))
            .ToListAsync(ct);
    }

    public async Task<Employee?> GetByIdAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
    {
        return await _db.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == employeeId, ct);
    }

    public async Task<IReadOnlyList<Employee>> ListActiveByLegalEntityAsync(
        Guid tenantId,
        Guid? legalEntityId,
        CancellationToken ct = default)
    {
        var query = _db.Employees.AsNoTracking().Where(e => e.TenantId == tenantId);

        if (legalEntityId is { } id)
            query = query.Where(e => e.LegalEntityId == id);

        return await query.OrderBy(e => e.FirstName).ThenBy(e => e.LastName).ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> ListLegalEntityChangeWarningsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> employeeIds,
        int year,
        CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
            return new Dictionary<Guid, string>();

        var yearStart = new DateOnly(year, 1, 1);
        var yearEnd = new DateOnly(year, 12, 31);

        var rows = await (
            from assignment in _db.PositionAssignments.AsNoTracking()
            join position in _db.Positions.AsNoTracking() on assignment.PositionId equals position.Id
            where assignment.TenantId == tenantId
                && employeeIds.Contains(assignment.EmployeeId)
                && assignment.AssignmentKind == PositionAssignmentKind.PrimaryEmployment
                && (assignment.AssignmentStatus == PositionAssignmentStatus.Active
                    || assignment.AssignmentStatus == PositionAssignmentStatus.Ended)
                && assignment.EffectiveFrom <= yearEnd
                && (assignment.EffectiveTo == null || assignment.EffectiveTo >= yearStart)
            orderby assignment.EmployeeId, assignment.EffectiveFrom
            select new { assignment.EmployeeId, assignment.EffectiveFrom, position.LegalEntityId })
            .ToListAsync(ct);

        return rows
            .GroupBy(x => x.EmployeeId)
            .Select(group =>
            {
                var ordered = group.ToList();
                for (var i = 1; i < ordered.Count; i++)
                {
                    if (ordered[i - 1].LegalEntityId != ordered[i].LegalEntityId)
                    {
                        return new
                        {
                            EmployeeId = group.Key,
                            Warning = LeaveEntitlementMessages.LegalEntityChanged(ordered[i].EffectiveFrom)
                        };
                    }
                }

                return null;
            })
            .Where(x => x is not null)
            .ToDictionary(x => x!.EmployeeId, x => x!.Warning);
    }
}
