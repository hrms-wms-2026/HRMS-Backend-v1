using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfWorkTaskRepository : IWorkTaskRepository
{
    private readonly ApplicationDbContext _db;

    public EfWorkTaskRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(WorkTask task, CancellationToken ct = default)
        => await _db.WorkTasks.AddAsync(task, ct);

    public async Task<WorkTask?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.WorkTasks.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == id, ct);

    public async Task<WorkTask?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.WorkTasks.FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == id, ct);

    public async Task<WorkTask?> GetTrackedByIdForTenantIncludingDeletedAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.WorkTasks.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == id, ct);

    public Task<bool> IsAssignedToEmployeeAsync(Guid taskId, Guid employeeId, CancellationToken ct = default)
        => _db.TaskAssignments.AsNoTracking()
            .AnyAsync(assignment => assignment.TaskId == taskId && assignment.EmployeeId == employeeId, ct);

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetAssignedEmployeeIdsByTaskIdsAsync(
        IReadOnlyCollection<Guid> taskIds, CancellationToken ct = default)
    {
        if (taskIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<Guid>>();
        var rows = await _db.TaskAssignments.AsNoTracking()
            .Where(assignment => taskIds.Contains(assignment.TaskId))
            .Select(assignment => new { assignment.TaskId, assignment.EmployeeId })
            .ToListAsync(ct);
        return rows.GroupBy(row => row.TaskId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<Guid>)group.Select(row => row.EmployeeId).ToList());
    }

    public async Task<IReadOnlyList<WorkTask>> GetByObjectiveIdAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default)
        => await _db.WorkTasks.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.ObjectiveId == objectiveId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<WorkTask>> GetByParentTaskIdAsync(Guid tenantId, Guid parentTaskId, CancellationToken ct = default)
        => await _db.WorkTasks.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.ParentTaskId == parentTaskId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<WorkTask>> GetTrackedByParentTaskIdAsync(Guid tenantId, Guid parentTaskId, CancellationToken ct = default)
        => await _db.WorkTasks
            .Where(t => t.TenantId == tenantId && t.ParentTaskId == parentTaskId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<WorkTask>> GetByProjectAsync(Guid tenantId, Guid projectId, CancellationToken ct = default)
        => await _db.WorkTasks.AsNoTracking()
            .Join(_db.Objectives,
                task => task.ObjectiveId,
                objective => objective.Id,
                (task, objective) => new { task, objective })
            .Where(x => x.task.TenantId == tenantId
                        && x.objective.TenantId == tenantId
                        && x.objective.ProjectId == projectId)
            .Select(x => x.task)
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, Guid>> GetObjectiveIdsByTaskIdsAsync(Guid tenantId, IReadOnlyList<Guid> taskIds, CancellationToken ct = default)
        => await _db.WorkTasks.AsNoTracking()
            .Where(t => t.TenantId == tenantId && taskIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.ObjectiveId, ct);

    public async Task<decimal> GetActiveAllocationSumByObjectiveIdAsync(Guid tenantId, Guid objectiveId, Guid? excludingTaskId = null, CancellationToken ct = default)
        => await _db.WorkTasks.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.ObjectiveId == objectiveId && t.Id != (excludingTaskId ?? Guid.Empty))
            .SumAsync(t => t.EstimatedHours ?? 0m, ct);

    public async Task<IReadOnlyList<WorkTask>> GetAssignedToEmployeeWithinRangeAsync(Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        return await _db.WorkTasks.AsNoTracking()
            .Where(t => t.TenantId == tenantId
                        && t.DueDate.HasValue
                        && t.DueDate >= from
                        && t.DueDate <= to
                        && _db.TaskAssignments.Any(a => a.TaskId == t.Id && a.EmployeeId == employeeId))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MyTaskRow>> GetMyActiveTasksAsync(Guid tenantId, Guid employeeId, DateOnly upcomingCutoff, CancellationToken ct = default)
    {
        return await (
            from t in _db.WorkTasks.AsNoTracking()
            join s in _db.TaskStatuses.AsNoTracking() on t.StatusId equals s.Id
            join p in _db.Projects.AsNoTracking() on t.ProjectId equals p.Id
            where t.TenantId == tenantId
                  && t.DueDate.HasValue
                  && t.DueDate <= upcomingCutoff
                  && !s.MarksTaskComplete
                  // A task can also reach 100% progress via the clock-in Push flow (see
                  // PushTaskCommandHandler) without anyone dragging it to a MarksTaskComplete
                  // status column - status is a manual/customizable signal, progress is the
                  // objective one, so either being "done" should exclude it from "my active tasks".
                  && t.ProgressPercent < 100
                  && _db.TaskAssignments.Any(a => a.TaskId == t.Id && a.EmployeeId == employeeId)
            orderby t.DueDate
            select new MyTaskRow(t.Id, t.ShortId, t.Title, t.DueDate!.Value, t.ProjectId, p.Name, t.ObjectiveId, t.Priority)
        ).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TaskProgressRow>> GetMyTaskProgressRowsAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
    {
        return await (
            from t in _db.WorkTasks.AsNoTracking()
            join s in _db.TaskStatuses.AsNoTracking() on t.StatusId equals s.Id
            where t.TenantId == tenantId
                  && _db.TaskAssignments.Any(a => a.TaskId == t.Id && a.EmployeeId == employeeId)
            select new TaskProgressRow(s.MarksTaskComplete, t.DueDate, t.ProgressPercent)
        ).ToListAsync(ct);
    }

    public async Task<OpenAssignedTasksPage> ListOpenAssignedToEmployeeAsync(Guid tenantId, Guid employeeId, int take, CancellationToken ct = default)
    {
        var query =
            from t in _db.WorkTasks.AsNoTracking()
            join s in _db.TaskStatuses.AsNoTracking() on t.StatusId equals s.Id
            where t.TenantId == tenantId
                  && (s.Category == TaskStatusCategories.NotStarted || s.Category == TaskStatusCategories.Active)
                  && _db.TaskAssignments.Any(a => a.TaskId == t.Id && a.EmployeeId == employeeId)
            select new { Task = t, s.Category, StatusName = s.Name };

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(x => x.Category)
            .ThenBy(x => x.Task.ShortId)
            .Take(take)
            .Select(x => new OpenAssignedTaskRow(x.Task.Id, x.Task.ShortId, x.Task.Title, x.Task.ProjectId, x.Task.ObjectiveId, x.Category,
                x.Task.DueDate, x.StatusName, x.Task.Priority))
            .ToListAsync(ct);

        return new OpenAssignedTasksPage(items, total);
    }

    public async Task<IReadOnlyDictionary<Guid, WorkTaskCounts>> CountByObjectivesAsync(
        Guid tenantId, IReadOnlyCollection<Guid> objectiveIds, DateOnly today, CancellationToken ct = default)
    {
        if (objectiveIds.Count == 0)
            return new Dictionary<Guid, WorkTaskCounts>();
        var rows = await (
            from t in _db.WorkTasks.AsNoTracking()
            join s in _db.TaskStatuses.AsNoTracking() on t.StatusId equals s.Id
            where t.TenantId == tenantId && t.ParentTaskId == null && objectiveIds.Contains(t.ObjectiveId)
            group new { s.Category, t.DueDate } by t.ObjectiveId into g
            select new
            {
                g.Key,
                Total = g.Count(),
                NotStarted = g.Count(x => x.Category == TaskStatusCategories.NotStarted),
                Active = g.Count(x => x.Category == TaskStatusCategories.Active),
                Done = g.Count(x => x.Category == TaskStatusCategories.Done),
                Overdue = g.Count(x => x.Category != TaskStatusCategories.Done && x.DueDate != null && x.DueDate < today)
            }).ToListAsync(ct);
        return rows.ToDictionary(r => r.Key, r => new WorkTaskCounts(r.Total, r.NotStarted, r.Active, r.Done, r.Overdue));
    }

    public async Task<IReadOnlyDictionary<Guid, WorkTaskCounts>> CountByProjectsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> projectIds, DateOnly today, CancellationToken ct = default)
    {
        if (projectIds.Count == 0)
            return new Dictionary<Guid, WorkTaskCounts>();
        var rows = await (
            from t in _db.WorkTasks.AsNoTracking()
            join s in _db.TaskStatuses.AsNoTracking() on t.StatusId equals s.Id
            where t.TenantId == tenantId && t.ParentTaskId == null && projectIds.Contains(t.ProjectId)
            group new { s.Category, t.DueDate } by t.ProjectId into g
            select new
            {
                g.Key,
                Total = g.Count(),
                NotStarted = g.Count(x => x.Category == TaskStatusCategories.NotStarted),
                Active = g.Count(x => x.Category == TaskStatusCategories.Active),
                Done = g.Count(x => x.Category == TaskStatusCategories.Done),
                Overdue = g.Count(x => x.Category != TaskStatusCategories.Done && x.DueDate != null && x.DueDate < today)
            }).ToListAsync(ct);
        return rows.ToDictionary(r => r.Key, r => new WorkTaskCounts(r.Total, r.NotStarted, r.Active, r.Done, r.Overdue));
    }

    public async Task<IReadOnlyList<EmployeeTaskPeriodRow>> ListForEmployeePeriodAsync(
        Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var fromUtc = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var toUtcExclusive = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        // `from` is a query keyword inside the query expression below, so capture the bounds under other names.
        var fromDate = from;
        var toDate = to;

        return await (
            from t in _db.WorkTasks.AsNoTracking()
            join s in _db.TaskStatuses.AsNoTracking() on t.StatusId equals s.Id
            join p in _db.Projects.AsNoTracking() on t.ProjectId equals p.Id
            // Left join: the module name is display-only and must never change which tasks are counted.
            join o in _db.Objectives.AsNoTracking() on t.ObjectiveId equals o.Id into objectives
            from o in objectives.DefaultIfEmpty()
            where t.TenantId == tenantId
                  && _db.TaskAssignments.Any(a => a.TaskId == t.Id && a.EmployeeId == employeeId)
                  && ((t.DueDate != null && t.DueDate >= fromDate && t.DueDate <= toDate)
                      || (t.CompletedAt != null && t.CompletedAt >= fromUtc && t.CompletedAt < toUtcExclusive)
                      || _db.TaskAssignments.Any(a => a.TaskId == t.Id && a.EmployeeId == employeeId
                                                      && a.AssignedAt >= fromUtc && a.AssignedAt < toUtcExclusive)
                      // Carried over: due before the period and not finished before it began. "Open" is
                      // judged by status/progress, not CompletedAt == null - tasks closed by status can lack
                      // a CompletedAt and must not resurface in every later period.
                      || (t.DueDate != null && t.DueDate < fromDate
                          && ((!s.MarksTaskComplete && t.ProgressPercent < 100)
                              || (t.CompletedAt != null && t.CompletedAt >= fromUtc))))
            select new EmployeeTaskPeriodRow(
                t.DueDate, t.CompletedAt, t.ProgressPercent, s.MarksTaskComplete, t.StoryPoints,
                t.Id, t.Title, t.ProjectId, p.Name,
                t.DueDate != null && t.DueDate < fromDate,
                t.ShortId, t.Priority, s.Name, s.Color, t.ObjectiveId, o != null ? o.Title : "")
        ).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<EmployeeWorkTaskRow>> ListOpenDueByAsync(Guid tenantId, Guid employeeId, DateOnly dueOnOrBefore, CancellationToken ct = default)
    {
        return await (
            from t in _db.WorkTasks.AsNoTracking()
            join s in _db.TaskStatuses.AsNoTracking() on t.StatusId equals s.Id
            join p in _db.Projects.AsNoTracking() on t.ProjectId equals p.Id
            where t.TenantId == tenantId
                  && t.DueDate.HasValue
                  && t.DueDate <= dueOnOrBefore
                  && !s.MarksTaskComplete
                  && t.ProgressPercent < 100
                  && _db.TaskAssignments.Any(a => a.TaskId == t.Id && a.EmployeeId == employeeId)
            orderby t.DueDate
            select new EmployeeWorkTaskRow(
                t.Id, t.ShortId, t.Title, t.ProjectId, p.Name, s.Name, s.Color, s.MarksTaskComplete,
                t.Priority, t.StoryPoints, t.DueDate, t.ProgressPercent, t.UpdatedAt ?? t.CreatedAt)
        ).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<EmployeeWorkTaskRow>> ListTodayAssignedAsync(
        Guid tenantId, Guid employeeId, DateOnly today, DateTimeOffset dayStartUtc, DateTimeOffset dayEndUtc, int take, CancellationToken ct = default)
    {
        return await (
            from t in _db.WorkTasks.AsNoTracking()
            join s in _db.TaskStatuses.AsNoTracking() on t.StatusId equals s.Id
            join p in _db.Projects.AsNoTracking() on t.ProjectId equals p.Id
            where t.TenantId == tenantId
                  && _db.TaskAssignments.Any(a => a.TaskId == t.Id && a.EmployeeId == employeeId)
                  && (t.DueDate == today
                      || ((t.UpdatedAt ?? t.CreatedAt) >= dayStartUtc && (t.UpdatedAt ?? t.CreatedAt) < dayEndUtc))
            orderby (t.UpdatedAt ?? t.CreatedAt) descending
            select new EmployeeWorkTaskRow(
                t.Id, t.ShortId, t.Title, t.ProjectId, p.Name, s.Name, s.Color, s.MarksTaskComplete,
                t.Priority, t.StoryPoints, t.DueDate, t.ProgressPercent, t.UpdatedAt ?? t.CreatedAt)
        ).Take(take).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DateTimeOffset>> ListCompletedAtForEmployeeAsync(Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, CancellationToken ct = default)
    {
        return await (
            from t in _db.WorkTasks.AsNoTracking()
            join s in _db.TaskStatuses.AsNoTracking() on t.StatusId equals s.Id
            where t.TenantId == tenantId
                  && t.CompletedAt.HasValue
                  && t.CompletedAt >= fromUtc
                  && t.CompletedAt < toUtcExclusive
                  && (s.MarksTaskComplete || t.ProgressPercent >= 100)
                  && _db.TaskAssignments.Any(a => a.TaskId == t.Id && a.EmployeeId == employeeId)
            select t.CompletedAt!.Value
        ).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<WorkTask>> GetBySprintIdAsync(Guid tenantId, Guid sprintId, CancellationToken ct = default)
        => await _db.WorkTasks.AsNoTracking().Where(t => t.TenantId == tenantId && t.SprintId == sprintId).ToListAsync(ct);

    // IgnoreQueryFilters() bypasses the soft-delete half of the composed query filter on
    // purpose: a status must stay undeletable if a soft-deleted task still references it, not
    // just active ones. Tenant scoping is preserved manually via the TenantId equality below.
    public async Task<bool> AnyActiveByStatusIdAsync(Guid tenantId, Guid statusId, CancellationToken ct = default)
        => await _db.WorkTasks.IgnoreQueryFilters()
            .AnyAsync(t => t.TenantId == tenantId && t.StatusId == statusId, ct);

    // IgnoreQueryFilters() bypasses the soft-delete half of the composed query filter on
    // purpose: a category must stay undeletable if a soft-deleted task still references it, not
    // just active ones. Tenant scoping is preserved manually via the TenantId equality below.
    public async Task<bool> AnyActiveByCategoryIdAsync(Guid tenantId, Guid categoryId, CancellationToken ct = default)
        => await _db.WorkTasks.IgnoreQueryFilters()
            .AnyAsync(t => t.TenantId == tenantId && t.CategoryId == categoryId, ct);

    public async Task<IReadOnlyList<LedTaskProgressRow>> ListTopLevelProgressRowsAsync(Guid tenantId, IReadOnlyCollection<Guid> objectiveIds, CancellationToken ct = default)
    {
        if (objectiveIds.Count == 0)
            return Array.Empty<LedTaskProgressRow>();
        var ids = objectiveIds.ToList();
        return await (
            from t in _db.WorkTasks.AsNoTracking()
            join s in _db.TaskStatuses.AsNoTracking() on t.StatusId equals s.Id
            where t.TenantId == tenantId && t.ParentTaskId == null && ids.Contains(t.ObjectiveId)
            select new LedTaskProgressRow(t.ObjectiveId, s.MarksTaskComplete, t.DueDate, t.ProgressPercent)
        ).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<LedOverdueTaskRow>> ListTopLevelOverdueAsync(Guid tenantId, IReadOnlyCollection<Guid> objectiveIds, DateOnly today, int take, CancellationToken ct = default)
    {
        if (objectiveIds.Count == 0 || take <= 0)
            return Array.Empty<LedOverdueTaskRow>();
        var ids = objectiveIds.ToList();
        // Same predicate as TaskProgressClassifier's Overdue bucket: not complete (status column or
        // 100%), and DueDate strictly before today.
        var rows = await (
            from t in _db.WorkTasks.AsNoTracking()
            join s in _db.TaskStatuses.AsNoTracking() on t.StatusId equals s.Id
            where t.TenantId == tenantId && t.ParentTaskId == null && ids.Contains(t.ObjectiveId)
                && !s.MarksTaskComplete && t.ProgressPercent < 100
                && t.DueDate != null && t.DueDate < today
            orderby t.DueDate, t.ShortId
            select new { t.Id, t.ShortId, t.Title, t.ProjectId, t.ObjectiveId, DueDate = t.DueDate!.Value }
        ).Take(take).ToListAsync(ct);

        var taskIds = rows.Select(r => r.Id).ToList();
        var assignees = taskIds.Count == 0
            ? new Dictionary<Guid, List<Guid>>()
            : (await _db.TaskAssignments.AsNoTracking().Where(a => taskIds.Contains(a.TaskId))
                    .Select(a => new { a.TaskId, a.EmployeeId }).ToListAsync(ct))
                .GroupBy(a => a.TaskId).ToDictionary(g => g.Key, g => g.Select(a => a.EmployeeId).ToList());

        return rows.Select(r => new LedOverdueTaskRow(
            r.Id, r.ShortId, r.Title, r.ProjectId, r.ObjectiveId, r.DueDate,
            assignees.TryGetValue(r.Id, out var assigneeIds) ? assigneeIds : new List<Guid>())).ToList();
    }

    public void Update(WorkTask task) => _db.WorkTasks.Update(task);
    public void Remove(WorkTask task) => _db.WorkTasks.Remove(task);
}
