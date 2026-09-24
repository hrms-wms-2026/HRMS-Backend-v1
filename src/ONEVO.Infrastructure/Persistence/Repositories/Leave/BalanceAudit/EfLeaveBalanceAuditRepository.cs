using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Leave.BalanceAudit.RepositoryInterfaces;

namespace ONEVO.Infrastructure.Persistence.Repositories.Leave.BalanceAudit;

public class EfLeaveBalanceAuditRepository : ILeaveBalanceAuditRepository
{
    private readonly ApplicationDbContext _db;

    public EfLeaveBalanceAuditRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<LeaveBalanceAuditRow>> ListRowsAsync(
        Guid tenantId, LeaveBalanceAuditListFilter filter, CancellationToken ct = default)
    {
        var query =
            from audit in _db.LeaveBalanceAudits.AsNoTracking()
            join employee in _db.Employees.AsNoTracking() on audit.EmployeeId equals employee.Id
            join leaveType in _db.LeaveTypes.AsNoTracking() on audit.LeaveTypeId equals leaveType.Id
            where audit.TenantId == tenantId
            select new { audit, employee, leaveType };

        if (filter.EmployeeId is { } employeeId)
            query = query.Where(x => x.audit.EmployeeId == employeeId);
        if (filter.LeaveTypeId is { } leaveTypeId)
            query = query.Where(x => x.audit.LeaveTypeId == leaveTypeId);
        if (!string.IsNullOrWhiteSpace(filter.ChangeType))
            query = query.Where(x => x.audit.ChangeType == filter.ChangeType);
        if (filter.FromDate is { } from)
        {
            var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(x => x.audit.CreatedAt >= fromUtc);
        }
        if (filter.ToDate is { } to)
        {
            var toUtc = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(x => x.audit.CreatedAt < toUtc);
        }

        var rows = await query
            .OrderByDescending(x => x.audit.CreatedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return rows.Select(x => new LeaveBalanceAuditRow(
            x.audit, x.employee.EmployeeNumber,
            $"{x.employee.FirstName} {x.employee.LastName}".Trim(),
            x.leaveType.Name, x.leaveType.Code)).ToList();
    }
}
