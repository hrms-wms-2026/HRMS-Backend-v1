using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Planner.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Planner.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Planner;

/// <summary>Day planner reads and writes. Task reads keep the default query filters, so meeting tasks never appear here.</summary>
public sealed class EfPlannerRepository : IPlannerRepository
{
    private readonly ApplicationDbContext _db;

    public EfPlannerRepository(ApplicationDbContext db) => _db = db;

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
        var rows = await _db.WorkTasks.AsNoTracking()
            .Where(t => t.TenantId == tenantId && taskIds.Contains(t.Id) && !t.IsDeleted)
            .Select(t => new
            {
                t.Id, t.Title, t.DueDate, t.EstimatedHours,
                Assigned = _db.TaskAssignments.Any(a => a.TaskId == t.Id && a.EmployeeId == employeeId),
            })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => new PlannerTaskInfo(r.Id, r.Title, r.DueDate, r.EstimatedHours, IsMeeting: false, r.Assigned));
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
            select new PlannerTaskInfo(t.Id, t.Title, t.DueDate, t.EstimatedHours, false, true))
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

    public async Task<PlannerAttendance?> GetAttendanceAsync(Guid tenantId, Guid employeeId, DateOnly date, CancellationToken ct = default)
        => await _db.AttendanceRecords.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.EmployeeId == employeeId && r.Date == date)
            .Select(r => new PlannerAttendance(r.ActualStart, r.ActualEnd, r.WorkedMinutes, r.BreakMinutes))
            .FirstOrDefaultAsync(ct);

    public void AddBlock(PlannedTaskBlock block) => _db.PlannedTaskBlocks.Add(block);

    public void RemoveBlock(PlannedTaskBlock block) => _db.PlannedTaskBlocks.Remove(block);
}
