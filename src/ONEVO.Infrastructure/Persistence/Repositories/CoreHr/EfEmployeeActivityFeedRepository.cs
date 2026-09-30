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
        rows.AddRange(await WorkAsync(tenantId, employeeId, userId, cutoff, take, ct));

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

    private async Task<List<EmployeeActivityRow>> WorkAsync(
        Guid tenantId, Guid employeeId, Guid userId, DateTimeOffset cutoff, int take, CancellationToken ct)
    {
        var rows = new List<EmployeeActivityRow>();
        static string Label(string shortId, string title) => $"{shortId} {title}";

        var created = await db.WorkTasks.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.CreatedById == userId && t.CreatedAt < cutoff)
            .OrderByDescending(t => t.CreatedAt).Take(take)
            .Select(t => new { t.Id, t.CreatedAt, t.ShortId, t.Title })
            .ToListAsync(ct);
        rows.AddRange(created.Select(t => new EmployeeActivityRow("task_created", t.Id, t.CreatedAt, Label(t.ShortId, t.Title), null)));

        var statusChanges = await (
            from l in db.TaskStatusChangeLogs.AsNoTracking()
            join t in db.WorkTasks.AsNoTracking() on l.TaskId equals t.Id
            join s in db.TaskStatuses.AsNoTracking() on l.ToStatusId equals s.Id
            where l.TenantId == tenantId && l.EmployeeId == employeeId && l.ChangedAt < cutoff
            orderby l.ChangedAt descending
            select new { l.Id, l.ChangedAt, t.ShortId, t.Title, ToStatus = s.Name }
        ).Take(take).ToListAsync(ct);
        rows.AddRange(statusChanges.Select(x => new EmployeeActivityRow(
            "task_status_changed", x.Id, x.ChangedAt, Label(x.ShortId, x.Title), $"→ {x.ToStatus}")));

        var edits = await (
            from l in db.TaskEditLogs.AsNoTracking()
            join t in db.WorkTasks.AsNoTracking() on l.TaskId equals t.Id
            where l.TenantId == tenantId && l.EmployeeId == employeeId && l.ChangedAt < cutoff
            orderby l.ChangedAt descending
            select new { l.Id, l.ChangedAt, t.ShortId, t.Title }
        ).Take(take).ToListAsync(ct);
        rows.AddRange(edits.Select(x => new EmployeeActivityRow("task_edited", x.Id, x.ChangedAt, Label(x.ShortId, x.Title), null)));

        var progress = await (
            from l in db.TaskPercentageLogs.AsNoTracking()
            join t in db.WorkTasks.AsNoTracking() on l.TaskId equals t.Id
            where l.TenantId == tenantId && l.EmployeeId == employeeId && l.ChangedAt < cutoff
            orderby l.ChangedAt descending
            select new { l.Id, l.ChangedAt, t.ShortId, t.Title, l.PreviousPercent, l.NewPercent }
        ).Take(take).ToListAsync(ct);
        rows.AddRange(progress.Select(x => new EmployeeActivityRow(
            "task_progress_changed", x.Id, x.ChangedAt, Label(x.ShortId, x.Title), $"{x.PreviousPercent}% → {x.NewPercent}%")));

        // Comment text is deliberately never selected.
        var comments = await (
            from c in db.TaskComments.AsNoTracking()
            join t in db.WorkTasks.AsNoTracking() on c.TaskId equals t.Id
            where c.TenantId == tenantId && c.EmployeeId == employeeId && c.CreatedAt < cutoff
            orderby c.CreatedAt descending
            select new { c.Id, c.CreatedAt, t.ShortId, t.Title }
        ).Take(take).ToListAsync(ct);
        rows.AddRange(comments.Select(x => new EmployeeActivityRow("task_commented", x.Id, x.CreatedAt, Label(x.ShortId, x.Title), null)));

        var clockIns = await (
            from s in db.TaskClockingSessions.AsNoTracking()
            join t in db.WorkTasks.AsNoTracking() on s.TaskId equals t.Id
            where s.TenantId == tenantId && s.EmployeeId == employeeId && s.ClockInAt < cutoff
            orderby s.ClockInAt descending
            select new { s.Id, At = s.ClockInAt, t.ShortId, t.Title }
        ).Take(take).ToListAsync(ct);
        rows.AddRange(clockIns.Select(x => new EmployeeActivityRow("task_clocked_in", x.Id, x.At, Label(x.ShortId, x.Title), null)));

        var clockOuts = await (
            from s in db.TaskClockingSessions.AsNoTracking()
            join t in db.WorkTasks.AsNoTracking() on s.TaskId equals t.Id
            where s.TenantId == tenantId && s.EmployeeId == employeeId && s.ClockOutAt != null && s.ClockOutAt < cutoff
            orderby s.ClockOutAt descending
            select new { s.Id, At = s.ClockOutAt!.Value, t.ShortId, t.Title }
        ).Take(take).ToListAsync(ct);
        rows.AddRange(clockOuts.Select(x => new EmployeeActivityRow("task_clocked_out", x.Id, x.At, Label(x.ShortId, x.Title), null)));

        var joins = await (
            from m in db.ProjectMembers.AsNoTracking()
            join o in db.Objectives.AsNoTracking() on m.ObjectiveId equals o.Id
            join p in db.Projects.AsNoTracking() on m.ProjectId equals p.Id
            where m.TenantId == tenantId && m.EmployeeId == employeeId && m.JoinedAt < cutoff
            orderby m.JoinedAt descending
            select new { m.Id, m.JoinedAt, o.IsDefault, ObjectiveTitle = o.Title, ProjectName = p.Name }
        ).Take(take).ToListAsync(ct);
        rows.AddRange(joins.Select(x => new EmployeeActivityRow(
            "module_joined", x.Id, x.JoinedAt, x.IsDefault ? x.ProjectName : x.ObjectiveTitle, null)));

        return rows;
    }
}
