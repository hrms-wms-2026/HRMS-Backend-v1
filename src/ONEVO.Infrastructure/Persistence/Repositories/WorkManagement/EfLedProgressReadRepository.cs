using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Leadership.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

/// <summary>Team Progress detail reads. Task set and classification mirror the
/// wm_module_hour_summaries trigger (20261020074523_AddModuleHourSummaries): top-level
/// (ParentTaskId null), not soft-deleted (query filter), Completed = status MarksTaskComplete or
/// ProgressPercent &gt;= 100, InProgress = otherwise ProgressPercent &gt; 0, else NotStarted; a
/// null estimate counts 0. An optional due-date window (period switch) keeps only tasks whose
/// DueDate is inside it, inclusive; undated tasks are then excluded. Every aggregate, filter, sort and page is translated to SQL - only the
/// requested page is materialized.</summary>
public sealed class EfLedProgressReadRepository(ApplicationDbContext db) : ILedProgressReadRepository
{
    private const int Completed = (int)LedTaskBucket.Completed;
    private const int InProgress = (int)LedTaskBucket.InProgress;
    private const int NotStarted = (int)LedTaskBucket.NotStarted;

    private sealed class ScopedTask
    {
        public Guid Id { get; init; }
        public decimal Hours { get; init; }
        public int Bucket { get; init; }
        public bool Unestimated { get; init; }
    }

    private sealed class TaskListRow
    {
        public Guid Id { get; init; }
        public string ShortId { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public int Bucket { get; init; }
        public string StatusName { get; init; } = string.Empty;
        public decimal Hours { get; init; }
        public DateOnly? DueDate { get; init; }
        public Guid ProjectId { get; init; }
        public string ProjectName { get; init; } = string.Empty;
        public Guid ObjectiveId { get; init; }
        public string ObjectiveTitle { get; init; } = string.Empty;
    }

    private sealed class EmployeeHoursAggregate
    {
        public Guid EmployeeId { get; init; }
        public string FirstName { get; init; } = string.Empty;
        public string LastName { get; init; } = string.Empty;
        public decimal Completed { get; init; }
        public decimal InProgress { get; init; }
        public decimal NotStarted { get; init; }
        public decimal Total { get; init; }
        public int TaskCount { get; init; }
        public int SharedTaskCount { get; init; }
        public decimal SharedHours { get; init; }
    }

    private IQueryable<ScopedTask> ScopedTasks(Guid tenantId, List<Guid> objectiveIds, LedDueWindow? dueWindow) =>
        from t in DueWithin(
            db.WorkTasks.AsNoTracking()
                .Where(t => t.TenantId == tenantId && t.ParentTaskId == null && objectiveIds.Contains(t.ObjectiveId)),
            dueWindow)
        join s in db.TaskStatuses.AsNoTracking() on t.StatusId equals s.Id
        select new ScopedTask
        {
            Id = t.Id,
            Hours = t.EstimatedHours ?? 0m,
            Bucket = s.MarksTaskComplete || t.ProgressPercent >= 100 ? Completed : t.ProgressPercent > 0 ? InProgress : NotStarted,
            Unestimated = t.EstimatedHours == null,
        };

    /// <summary>Inclusive DueDate window; null = no filter. Undated tasks never fall inside a window.</summary>
    private static IQueryable<WorkTask> DueWithin(
        IQueryable<WorkTask> tasks, LedDueWindow? dueWindow)
    {
        if (dueWindow is null)
            return tasks;
        var from = dueWindow.From;
        var to = dueWindow.To;
        return tasks.Where(t => t.DueDate != null && t.DueDate >= from && t.DueDate <= to);
    }

    public async Task<LedPagedRows<LedEmployeeHoursRow>> ListEmployeeHoursAsync(
        Guid tenantId, LedEmployeeHoursFilter filter, CancellationToken ct = default)
    {
        if (filter.ObjectiveIds.Count == 0 || filter.Take <= 0)
            return new LedPagedRows<LedEmployeeHoursRow>([], 0);

        var tasks = ScopedTasks(tenantId, filter.ObjectiveIds.ToList(), filter.DueWindow);
        var assigneeCounts =
            from a in db.TaskAssignments.AsNoTracking()
            group a by a.TaskId into g
            select new { TaskId = g.Key, Count = g.Count() };

        // One row per (assignee, task): a shared task contributes its FULL estimate to every assignee.
        var perAssignee =
            from a in db.TaskAssignments.AsNoTracking()
            join t in tasks on a.TaskId equals t.Id
            join c in assigneeCounts on a.TaskId equals c.TaskId
            select new { a.EmployeeId, t.Hours, t.Bucket, Shared = c.Count > 1 };

        var grouped =
            from x in perAssignee
            group x by x.EmployeeId into g
            select new
            {
                EmployeeId = g.Key,
                Completed = g.Sum(x => x.Bucket == Completed ? x.Hours : 0m),
                InProgress = g.Sum(x => x.Bucket == InProgress ? x.Hours : 0m),
                NotStarted = g.Sum(x => x.Bucket == NotStarted ? x.Hours : 0m),
                Total = g.Sum(x => x.Hours),
                TaskCount = g.Count(),
                SharedTaskCount = g.Count(x => x.Shared),
                SharedHours = g.Sum(x => x.Shared ? x.Hours : 0m),
            };

        var rows =
            from g in grouped
            join e in db.Employees.AsNoTracking() on g.EmployeeId equals e.Id
            where e.TenantId == tenantId
            select new EmployeeHoursAggregate
            {
                EmployeeId = g.EmployeeId, FirstName = e.FirstName, LastName = e.LastName,
                Completed = g.Completed, InProgress = g.InProgress, NotStarted = g.NotStarted, Total = g.Total,
                TaskCount = g.TaskCount, SharedTaskCount = g.SharedTaskCount, SharedHours = g.SharedHours,
            };

        if (filter.Search is { Length: > 0 } search)
        {
            // .ToLower().Contains rather than ILike: Contains is parameterized and escapes LIKE
            // wildcards, matching the other paged searches in this layer.
            var term = search.ToLower();
            rows = rows.Where(r => (r.FirstName + " " + r.LastName).ToLower().Contains(term));
        }

        var total = await rows.CountAsync(ct);
        if (total == 0 || filter.Skip >= total)
            return new LedPagedRows<LedEmployeeHoursRow>([], total);

        var page = await SortEmployees(rows, filter.Sort, filter.Descending)
            .Skip(filter.Skip).Take(filter.Take)
            .ToListAsync(ct);

        return new LedPagedRows<LedEmployeeHoursRow>(page.Select(r => new LedEmployeeHoursRow(
            r.EmployeeId, $"{r.FirstName} {r.LastName}".Trim(), r.Completed, r.InProgress, r.NotStarted, r.Total,
            r.TaskCount, r.SharedTaskCount, r.SharedHours)).ToList(), total);
    }

    private static IQueryable<EmployeeHoursAggregate> SortEmployees(
        IQueryable<EmployeeHoursAggregate> rows, LedEmployeeHoursSort sort, bool descending)
    {
        IOrderedQueryable<EmployeeHoursAggregate> ordered = sort switch
        {
            LedEmployeeHoursSort.Name => descending
                ? rows.OrderByDescending(r => r.FirstName).ThenByDescending(r => r.LastName)
                : rows.OrderBy(r => r.FirstName).ThenBy(r => r.LastName),
            LedEmployeeHoursSort.CompletedHours => descending ? rows.OrderByDescending(r => r.Completed) : rows.OrderBy(r => r.Completed),
            LedEmployeeHoursSort.InProgressHours => descending ? rows.OrderByDescending(r => r.InProgress) : rows.OrderBy(r => r.InProgress),
            LedEmployeeHoursSort.NotStartedHours => descending ? rows.OrderByDescending(r => r.NotStarted) : rows.OrderBy(r => r.NotStarted),
            _ => descending ? rows.OrderByDescending(r => r.Total) : rows.OrderBy(r => r.Total),
        };
        // Deterministic paging: equal values fall back to name, then the unique employee id.
        return ordered.ThenBy(r => r.FirstName).ThenBy(r => r.LastName).ThenBy(r => r.EmployeeId);
    }

    public async Task<LedUnassignedHoursRow> GetUnassignedHoursAsync(
        Guid tenantId, IReadOnlyCollection<Guid> objectiveIds, LedDueWindow? dueWindow, CancellationToken ct = default)
    {
        if (objectiveIds.Count == 0)
            return new LedUnassignedHoursRow(0, 0, 0, 0, 0);

        var totals = await ScopedTasks(tenantId, objectiveIds.ToList(), dueWindow)
            .Where(t => !db.TaskAssignments.Any(a => a.TaskId == t.Id))
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Completed = g.Sum(x => x.Bucket == Completed ? x.Hours : 0m),
                InProgress = g.Sum(x => x.Bucket == InProgress ? x.Hours : 0m),
                NotStarted = g.Sum(x => x.Bucket == NotStarted ? x.Hours : 0m),
                Total = g.Sum(x => x.Hours),
                Count = g.Count(),
            })
            .FirstOrDefaultAsync(ct);

        return totals is null
            ? new LedUnassignedHoursRow(0, 0, 0, 0, 0)
            : new LedUnassignedHoursRow(totals.Completed, totals.InProgress, totals.NotStarted, totals.Total, totals.Count);
    }

    public async Task<LedPagedRows<LedProgressTaskRow>> ListTasksAsync(
        Guid tenantId, LedProgressTaskFilter filter, CancellationToken ct = default)
    {
        if (filter.ObjectiveIds.Count == 0 || filter.Take <= 0)
            return new LedPagedRows<LedProgressTaskRow>([], 0);

        var ids = filter.ObjectiveIds.ToList();
        var query =
            from t in DueWithin(
                db.WorkTasks.AsNoTracking().Where(t => t.TenantId == tenantId && t.ParentTaskId == null && ids.Contains(t.ObjectiveId)),
                filter.DueWindow)
            join s in db.TaskStatuses.AsNoTracking() on t.StatusId equals s.Id
            join p in db.Projects.AsNoTracking() on t.ProjectId equals p.Id
            join o in db.Objectives.AsNoTracking() on t.ObjectiveId equals o.Id
            select new TaskListRow
            {
                Id = t.Id, ShortId = t.ShortId, Title = t.Title,
                Bucket = s.MarksTaskComplete || t.ProgressPercent >= 100 ? Completed : t.ProgressPercent > 0 ? InProgress : NotStarted,
                StatusName = s.Name, Hours = t.EstimatedHours ?? 0m, DueDate = t.DueDate,
                ProjectId = t.ProjectId, ProjectName = p.Name, ObjectiveId = t.ObjectiveId, ObjectiveTitle = o.Title,
            };

        if (filter.Status is LedTaskBucket status)
        {
            var bucket = (int)status;
            query = query.Where(t => t.Bucket == bucket);
        }

        if (filter.EmployeeId is Guid employeeId)
            query = query.Where(t => db.TaskAssignments.Any(a => a.TaskId == t.Id && a.EmployeeId == employeeId));

        if (filter.Search is { Length: > 0 } search)
        {
            var term = search.ToLower();
            query = query.Where(t => t.Title.ToLower().Contains(term) || t.ShortId.ToLower().Contains(term));
        }

        var total = await query.CountAsync(ct);
        if (total == 0 || filter.Skip >= total)
            return new LedPagedRows<LedProgressTaskRow>([], total);

        var page = await SortTasks(query, filter.Sort, filter.Descending)
            .Skip(filter.Skip).Take(filter.Take)
            .ToListAsync(ct);

        var taskIds = page.Select(t => t.Id).ToList();
        var assignments = await (
            from a in db.TaskAssignments.AsNoTracking()
            where taskIds.Contains(a.TaskId)
            join e in db.Employees.AsNoTracking() on a.EmployeeId equals e.Id into employees
            from e in employees.DefaultIfEmpty()
            select new { a.TaskId, a.EmployeeId, FirstName = e == null ? null : e.FirstName, LastName = e == null ? null : e.LastName }
        ).ToListAsync(ct);
        var byTask = assignments.GroupBy(a => a.TaskId).ToDictionary(
            g => g.Key,
            g => g.Select(a => new LedProgressTaskAssigneeRow(
                    a.EmployeeId,
                    a.FirstName is null && a.LastName is null ? "Unknown employee" : $"{a.FirstName} {a.LastName}".Trim()))
                .OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.EmployeeId)
                .ToList());

        return new LedPagedRows<LedProgressTaskRow>(page.Select(t =>
        {
            var assignees = byTask.TryGetValue(t.Id, out var list) ? list : [];
            return new LedProgressTaskRow(
                t.Id, t.ShortId, t.Title, (LedTaskBucket)t.Bucket, t.StatusName, t.Hours, t.DueDate,
                t.ProjectId, t.ProjectName, t.ObjectiveId, t.ObjectiveTitle, assignees, assignees.Count);
        }).ToList(), total);
    }

    public async Task<LedPeriodTotalsRow> GetPeriodTotalsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> objectiveIds, LedDueWindow dueWindow, CancellationToken ct = default)
    {
        if (objectiveIds.Count == 0)
            return LedPeriodTotalsRow.Zero;

        var totals = await ScopedTasks(tenantId, objectiveIds.ToList(), dueWindow)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Completed = g.Sum(x => x.Bucket == Completed ? x.Hours : 0m),
                InProgress = g.Sum(x => x.Bucket == InProgress ? x.Hours : 0m),
                NotStarted = g.Sum(x => x.Bucket == NotStarted ? x.Hours : 0m),
                Total = g.Sum(x => x.Hours),
                Count = g.Count(),
                CompletedCount = g.Count(x => x.Bucket == Completed),
                InProgressCount = g.Count(x => x.Bucket == InProgress),
                NotStartedCount = g.Count(x => x.Bucket == NotStarted),
                UnestimatedCount = g.Count(x => x.Unestimated),
            })
            .FirstOrDefaultAsync(ct);

        return totals is null
            ? LedPeriodTotalsRow.Zero
            : new LedPeriodTotalsRow(totals.Completed, totals.InProgress, totals.NotStarted, totals.Total, totals.Count,
                totals.CompletedCount, totals.InProgressCount, totals.NotStartedCount, totals.UnestimatedCount);
    }

    private static IQueryable<TaskListRow> SortTasks(IQueryable<TaskListRow> query, LedProgressTaskSort sort, bool descending)
    {
        IOrderedQueryable<TaskListRow> ordered = sort switch
        {
            // Undated tasks always sort last, whichever direction.
            LedProgressTaskSort.DueDate => descending
                ? query.OrderBy(t => t.DueDate == null).ThenByDescending(t => t.DueDate)
                : query.OrderBy(t => t.DueDate == null).ThenBy(t => t.DueDate),
            LedProgressTaskSort.Title => descending ? query.OrderByDescending(t => t.Title) : query.OrderBy(t => t.Title),
            LedProgressTaskSort.Status => descending
                ? query.OrderByDescending(t => t.Bucket).ThenByDescending(t => t.StatusName)
                : query.OrderBy(t => t.Bucket).ThenBy(t => t.StatusName),
            _ => descending ? query.OrderByDescending(t => t.Hours) : query.OrderBy(t => t.Hours),
        };
        // Deterministic paging: ShortId is unique per project, Id breaks any remaining tie.
        return ordered.ThenBy(t => t.ShortId).ThenBy(t => t.Id);
    }
}
