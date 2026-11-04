using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Planner.RepositoryInterfaces;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.WorkManagement.Planner.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Planner;

/// <summary>Day planner reads and writes. Task reads keep the default query filters, so meeting tasks never appear here.</summary>
public sealed class EfPlannerRepository : IPlannerRepository
{
    private readonly ApplicationDbContext _db;

    public EfPlannerRepository(ApplicationDbContext db) => _db = db;

    /// <summary>The advisory-lock key of one employee's plan.</summary>
    public static string PlanLockKey(Guid tenantId, Guid employeeId) => $"planner:{tenantId}:{employeeId}";

    public async Task LockPlanAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
    {
        if (_db.Database.CurrentTransaction is null)
            throw new InvalidOperationException($"{nameof(LockPlanAsync)} needs a transaction.");
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({PlanLockKey(tenantId, employeeId)}, 0))", ct);
    }

    public async Task<IReadOnlyList<PlannedTaskBlock>> ListBlocksAsync(Guid tenantId, Guid employeeId, DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = (from.ToUniversalTime(), to.ToUniversalTime());
        return await _db.PlannedTaskBlocks.AsNoTracking()
            .Where(b => b.TenantId == tenantId && b.EmployeeId == employeeId && !b.IsDeleted && b.StartAt < toUtc && b.EndAt > fromUtc)
            .OrderBy(b => b.StartAt).ToListAsync(ct);
    }

    public async Task<PlannedTaskBlock?> GetBlockTrackedAsync(Guid tenantId, Guid employeeId, Guid blockId, CancellationToken ct = default)
        => await _db.PlannedTaskBlocks
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.EmployeeId == employeeId && b.Id == blockId && !b.IsDeleted, ct);

    public async Task<bool> OverlapsAsync(Guid tenantId, Guid employeeId, DateTimeOffset start, DateTimeOffset end, Guid? exceptBlockId,
        CancellationToken ct = default)
    {
        var (startUtc, endUtc) = (start.ToUniversalTime(), end.ToUniversalTime());
        return await _db.PlannedTaskBlocks.AnyAsync(b => b.TenantId == tenantId && b.EmployeeId == employeeId && !b.IsDeleted
            && b.Id != exceptBlockId && b.StartAt < endUtc && b.EndAt > startUtc, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, PlannerTaskInfo>> GetTasksAsync(Guid tenantId, Guid employeeId,
        IReadOnlyCollection<Guid> taskIds, CancellationToken ct = default)
    {
        if (taskIds.Count == 0)
            return new Dictionary<Guid, PlannerTaskInfo>();
        var rows = await (
            from t in _db.WorkTasks.AsNoTracking()
            join p in _db.Projects.AsNoTracking() on t.ProjectId equals p.Id
            join c in _db.TaskCategories.AsNoTracking() on t.CategoryId equals c.Id into categories
            from c in categories.DefaultIfEmpty()
            where t.TenantId == tenantId && taskIds.Contains(t.Id) && !t.IsDeleted
            select new
            {
                t.Id, t.Title, t.DueDate, t.EstimatedHours, t.ProjectId, ProjectName = p.Name,
                CategoryName = c == null ? "" : c.Name, CategoryKind = c == null ? TaskCategoryKinds.Task : c.Kind,
                Assigned = _db.TaskAssignments.Any(a => a.TaskId == t.Id && a.EmployeeId == employeeId),
            })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Id,
            r => new PlannerTaskInfo(r.Id, r.Title, r.DueDate, r.EstimatedHours, IsMeeting: false, r.Assigned, r.ProjectId, r.ProjectName,
                r.CategoryName, r.CategoryKind));
    }

    public async Task<IReadOnlyList<PlannerTaskInfo>> ListDueAssignedTasksAsync(Guid tenantId, Guid employeeId, DateOnly date,
        CancellationToken ct = default)
        => await (
            from t in _db.WorkTasks.AsNoTracking()
            join s in _db.TaskStatuses.AsNoTracking() on t.StatusId equals s.Id into statuses
            from s in statuses.DefaultIfEmpty()
            where t.TenantId == tenantId && !t.IsDeleted && t.DueDate != null && t.DueDate <= date
                  && t.ProgressPercent < 100 && (s == null || !s.MarksTaskComplete)
                  && _db.TaskAssignments.Any(a => a.TaskId == t.Id && a.EmployeeId == employeeId)
            orderby t.DueDate, t.Title
            select new PlannerTaskInfo(t.Id, t.Title, t.DueDate, t.EstimatedHours, false, true, t.ProjectId))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PlannerSession>> ListSessionsAsync(Guid tenantId, Guid employeeId, DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = (from.ToUniversalTime(), to.ToUniversalTime());
        return await _db.TaskClockingSessions.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.EmployeeId == employeeId && !s.IsDeleted
                && s.ClockInAt < toUtc && (s.ClockOutAt == null || s.ClockOutAt > fromUtc))
            .Select(s => new PlannerSession(s.TaskId, s.ClockInAt, s.ClockOutAt))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PlannerFocusTaskRow>> ListFocusTasksAsync(Guid tenantId, Guid employeeId, DateTimeOffset now,
        CancellationToken ct = default)
    {
        // Same "not done" test as ListDueAssignedTasksAsync; the default query filters already leave out meeting tasks.
        var tasks = await (
            from t in _db.WorkTasks.AsNoTracking()
            join p in _db.Projects.AsNoTracking() on t.ProjectId equals p.Id
            join s in _db.TaskStatuses.AsNoTracking() on t.StatusId equals s.Id into statuses
            from s in statuses.DefaultIfEmpty()
            join c in _db.TaskCategories.AsNoTracking() on t.CategoryId equals c.Id into categories
            from c in categories.DefaultIfEmpty()
            where t.TenantId == tenantId && !t.IsDeleted && t.ProgressPercent < 100 && (s == null || !s.MarksTaskComplete)
                  && _db.TaskAssignments.Any(a => a.TaskId == t.Id && a.EmployeeId == employeeId)
            select new
            {
                t.Id, t.ProjectId, ProjectName = p.Name, t.Title, t.Priority, t.DueDate, t.EstimatedHours, t.CreatedAt,
                CategoryName = c == null ? "" : c.Name, CategoryKind = c == null ? TaskCategoryKinds.Task : c.Kind,
            })
            .ToListAsync(ct);
        if (tasks.Count == 0)
            return [];

        var ids = tasks.Select(t => t.Id).ToList();
        // Everyone's sessions on these tasks: the estimate belongs to the task, not to one assignee (design call C4).
        var sessions = await _db.TaskClockingSessions.AsNoTracking()
            .Where(s => s.TenantId == tenantId && ids.Contains(s.TaskId) && !s.IsDeleted)
            .Select(s => new { s.TaskId, s.ClockInAt, s.ClockOutAt })
            .ToListAsync(ct);
        var logged = sessions.GroupBy(s => s.TaskId).ToDictionary(g => g.Key,
            g => g.Sum(s => Math.Max(0, (int)((s.ClockOutAt ?? now) - s.ClockInAt).TotalMinutes)));

        return tasks.Select(t => new PlannerFocusTaskRow(t.Id, t.ProjectId, t.ProjectName, t.Title, t.Priority, t.DueDate,
            t.EstimatedHours, logged.GetValueOrDefault(t.Id), t.CategoryName, t.CategoryKind, t.CreatedAt)).ToList();
    }

    public async Task<LegalEntity?> GetEmployeeLegalEntityAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
    {
        var own = await (
            from e in _db.Employees.AsNoTracking()
            join le in _db.LegalEntities.AsNoTracking() on e.LegalEntityId equals le.Id
            where e.TenantId == tenantId && e.Id == employeeId && le.TenantId == tenantId
            select le).FirstOrDefaultAsync(ct);
        return own ?? await _db.LegalEntities.AsNoTracking().FirstOrDefaultAsync(le => le.TenantId == tenantId && le.IsPrimary, ct);
    }

    public async Task<PlannerAttendance?> GetAttendanceAsync(Guid tenantId, Guid employeeId, DateOnly date, CancellationToken ct = default)
        => await _db.AttendanceRecords.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.EmployeeId == employeeId && r.Date == date)
            .Select(r => new PlannerAttendance(r.ActualStart, r.ActualEnd, r.WorkedMinutes, r.BreakMinutes))
            .FirstOrDefaultAsync(ct);

    public void AddBlock(PlannedTaskBlock block) => _db.PlannedTaskBlocks.Add(block);

    public void RemoveBlock(PlannedTaskBlock block) => _db.PlannedTaskBlocks.Remove(block);
}
