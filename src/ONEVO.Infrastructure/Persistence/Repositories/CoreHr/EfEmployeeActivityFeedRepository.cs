using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;

namespace ONEVO.Infrastructure.Persistence.Repositories.CoreHr;

/// <summary>
/// Union read behind the Overview "Recent Activity" widget. Each source is one small query
/// returning at most `take` rows older than the cursor; the merged top `take` is exact because the
/// overall top `take` is always inside each source's own top `take`.
/// </summary>
public sealed class EfEmployeeActivityFeedRepository(ApplicationDbContext db) : IEmployeeActivityFeedRepository
{
    public async Task<IReadOnlyList<EmployeeActivityRow>> ListAsync(
        Guid tenantId, Guid employeeId, Guid userId, DateTimeOffset? before, int take, CancellationToken ct = default)
    {
        var cutoff = before ?? DateTimeOffset.MaxValue;
        var rows = new List<EmployeeActivityRow>();

        rows.AddRange(await AttendanceAndLeaveAsync(tenantId, employeeId, cutoff, take, ct));

        return rows
            .OrderByDescending(r => r.At)
            .ThenBy(r => r.Kind, StringComparer.Ordinal)
            .Take(take)
            .ToList();
    }

    private async Task<List<EmployeeActivityRow>> AttendanceAndLeaveAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset cutoff, int take, CancellationToken ct)
    {
        var rows = new List<EmployeeActivityRow>();

        rows.AddRange(await db.AttendanceRecords.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.EmployeeId == employeeId && r.ActualStart != null && r.ActualStart < cutoff)
            .OrderByDescending(r => r.ActualStart).Take(take)
            .Select(r => new EmployeeActivityRow("attendance_clock_in", r.Id, r.ActualStart!.Value, null, null))
            .ToListAsync(ct));

        rows.AddRange(await db.AttendanceRecords.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.EmployeeId == employeeId && r.ActualEnd != null && r.ActualEnd < cutoff)
            .OrderByDescending(r => r.ActualEnd).Take(take)
            .Select(r => new EmployeeActivityRow("attendance_clock_out", r.Id, r.ActualEnd!.Value, null, null))
            .ToListAsync(ct));

        rows.AddRange(await (
            from r in db.LeaveRequests.AsNoTracking()
            join t in db.LeaveTypes.AsNoTracking() on r.LeaveTypeId equals t.Id
            where r.TenantId == tenantId && r.EmployeeId == employeeId && r.CreatedAt < cutoff
            orderby r.CreatedAt descending
            select new EmployeeActivityRow("leave_requested", r.Id, r.CreatedAt, t.Name, null)
        ).Take(take).ToListAsync(ct));

        var corrections = await db.AttendanceCorrections.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.EmployeeId == employeeId && c.CreatedAt < cutoff)
            .OrderByDescending(c => c.CreatedAt).Take(take)
            .Select(c => new { c.Id, c.CreatedAt, c.WorkDate })
            .ToListAsync(ct);
        rows.AddRange(corrections.Select(c => new EmployeeActivityRow(
            "attendance_correction_requested", c.Id, c.CreatedAt, c.WorkDate.ToString("yyyy-MM-dd"), null)));

        rows.AddRange(await db.WorkAreaChangeRequests.AsNoTracking()
            .Where(w => w.TenantId == tenantId && w.EmployeeId == employeeId && w.RequestedAt < cutoff)
            .OrderByDescending(w => w.RequestedAt).Take(take)
            .Select(w => new EmployeeActivityRow("work_area_change_requested", w.Id, w.RequestedAt, w.RequestedWorkModeName, null))
            .ToListAsync(ct));

        rows.AddRange(await db.LocationChangeRequests.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.EmployeeId == employeeId && l.RequestedAt < cutoff)
            .OrderByDescending(l => l.RequestedAt).Take(take)
            .Select(l => new EmployeeActivityRow("location_change_requested", l.Id, l.RequestedAt, null, null))
            .ToListAsync(ct));

        return rows;
    }
}
