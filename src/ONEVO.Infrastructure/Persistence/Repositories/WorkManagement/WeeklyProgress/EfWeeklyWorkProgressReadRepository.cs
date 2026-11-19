using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.WeeklyProgress.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.WeeklyProgress;

/// <summary>Weekly Work Progress reads. Task reads keep the default query filters (meeting tasks hidden) and also exclude
/// them explicitly, so a filter change can't leak meetings into the task weightage.</summary>
public sealed class EfWeeklyWorkProgressReadRepository : IWeeklyWorkProgressReadRepository
{
    private readonly ApplicationDbContext _db;

    public EfWeeklyWorkProgressReadRepository(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<WeekTaskWeight>> ListWeekTaskWeightsAsync(Guid tenantId, Guid employeeId, DateOnly weekStart,
        DateOnly weekEnd, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        var (rangeStart, rangeEnd) = (fromUtc.ToUniversalTime(), toUtc.ToUniversalTime());
        return await (
            from t in _db.WorkTasks.AsNoTracking()
            join s in _db.TaskStatuses.AsNoTracking() on t.StatusId equals s.Id into statuses
            from s in statuses.DefaultIfEmpty()
            where t.TenantId == tenantId && !t.IsDeleted && t.TaskKind != WorkTaskKinds.Meeting
                  && _db.TaskAssignments.Any(a => a.TaskId == t.Id && a.EmployeeId == employeeId)
                  && ((t.DueDate >= weekStart && t.DueDate <= weekEnd)
                      || _db.PlannedTaskBlocks.Any(b => b.TaskId == t.Id && b.EmployeeId == employeeId && !b.IsDeleted
                          && b.StartAt < rangeEnd && b.EndAt > rangeStart)
                      || _db.TaskClockingSessions.Any(c => c.TaskId == t.Id && c.EmployeeId == employeeId && !c.IsDeleted
                          && c.ClockInAt < rangeEnd && (c.ClockOutAt == null || c.ClockOutAt > rangeStart)))
            select new WeekTaskWeight(t.Id, t.EstimatedHours, s == null ? TaskStatusCategories.NotStarted : s.Category))
            .ToListAsync(ct);
    }
}
