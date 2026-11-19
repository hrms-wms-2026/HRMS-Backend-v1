using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.TimeAttendance.Team.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance;

/// <summary>Completed top-level tasks per assignee (see <see cref="ITeamCompletedTaskReader"/>). Keeps the default query
/// filters and also excludes deleted and meeting tasks explicitly, as EfWeeklyWorkProgressReadRepository does.</summary>
public sealed class EfTeamCompletedTaskReader(ApplicationDbContext db) : ITeamCompletedTaskReader
{
    public async Task<IReadOnlyList<TeamCompletedTask>> ListCompletedAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds,
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
            return [];
        var (start, end) = (fromUtc.ToUniversalTime(), toUtc.ToUniversalTime());
        var ids = employeeIds.ToList();
        return await (
            from t in db.WorkTasks.AsNoTracking()
            join a in db.TaskAssignments.AsNoTracking() on t.Id equals a.TaskId
            join s in db.TaskStatuses.AsNoTracking() on t.StatusId equals s.Id into statuses
            from s in statuses.DefaultIfEmpty()
            where t.TenantId == tenantId && !t.IsDeleted && t.TaskKind != WorkTaskKinds.Meeting && t.ParentTaskId == null
                  && ids.Contains(a.EmployeeId)
                  && t.CompletedAt != null && t.CompletedAt >= start && t.CompletedAt < end
                  && ((s != null && s.MarksTaskComplete) || t.ProgressPercent >= 100)
            select new TeamCompletedTask(t.Id, a.EmployeeId, t.EstimatedHours, t.CompletedAt!.Value))
            .ToListAsync(ct);
    }
}
