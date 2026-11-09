using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.TimeAttendance.Team.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance;

/// <summary>See <see cref="IForecastTaskReader"/>. Keeps the default query filters and also excludes deleted rows and
/// meeting tasks explicitly, as EfTeamCompletedTaskReader does.</summary>
public sealed class EfForecastTaskReader(ApplicationDbContext db) : IForecastTaskReader
{
    public async Task<IReadOnlyList<ForecastOpenTask>> ListOpenAssignedAsync(Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> employeeIds,
        CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
            return [];
        var ids = employeeIds.ToList();
        var rows = await (
            from t in db.WorkTasks.AsNoTracking()
            join a in db.TaskAssignments.AsNoTracking() on t.Id equals a.TaskId
            join o in db.Objectives.AsNoTracking() on t.ObjectiveId equals o.Id
            join p in db.Projects.AsNoTracking() on t.ProjectId equals p.Id
            join s in db.TaskStatuses.AsNoTracking() on t.StatusId equals s.Id into statuses
            from s in statuses.DefaultIfEmpty()
            where t.TenantId == tenantId && !t.IsDeleted && t.TaskKind != WorkTaskKinds.Meeting
                  && ids.Contains(a.EmployeeId)
                  && o.TenantId == tenantId && o.IsActive && !o.IsDeleted
                  && p.TenantId == tenantId && p.IsActive && !p.IsDeleted && p.SystemPurpose == null && p.OwningLegalEntityId == legalEntityId
                  && !(s != null && s.MarksTaskComplete)
            select new { t.Id, a.EmployeeId, t.ProjectId, ProjectName = p.Name, ModuleId = o.Id, ModuleTitle = o.Title, o.EndDate,
                t.DueDate, t.EstimatedHours, t.CompletedHours })
            .ToListAsync(ct);
        if (rows.Count == 0)
            return [];

        var taskIds = rows.Select(r => r.Id).Distinct().ToList();
        var assigneeCounts = await db.TaskAssignments.AsNoTracking()
            .Where(a => taskIds.Contains(a.TaskId))
            .GroupBy(a => a.TaskId)
            .Select(g => new { TaskId = g.Key, Count = g.Select(a => a.EmployeeId).Distinct().Count() })
            .ToDictionaryAsync(x => x.TaskId, x => x.Count, ct);

        return rows
            .Select(r => new ForecastOpenTask(r.Id, r.EmployeeId, r.ProjectId, r.ProjectName, r.ModuleId, r.ModuleTitle, r.EndDate,
                r.DueDate, r.EstimatedHours, r.CompletedHours, assigneeCounts.GetValueOrDefault(r.Id, 1)))
            .DistinctBy(r => (r.TaskId, r.EmployeeId))
            .ToList();
    }

    public async Task<IReadOnlyList<ForecastMemberModule>> ListMemberModulesAsync(Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> employeeIds,
        CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
            return [];
        var ids = employeeIds.ToList();
        var rows = await (
            from m in db.ProjectMembers.AsNoTracking()
            join o in db.Objectives.AsNoTracking() on m.ObjectiveId equals o.Id
            join p in db.Projects.AsNoTracking() on m.ProjectId equals p.Id
            where m.TenantId == tenantId && m.IsActive && !m.IsDeleted && ids.Contains(m.EmployeeId)
                  && o.TenantId == tenantId && o.IsActive && !o.IsDeleted
                  && p.TenantId == tenantId && p.IsActive && !p.IsDeleted && p.SystemPurpose == null && p.OwningLegalEntityId == legalEntityId
            select new ForecastMemberModule(m.EmployeeId, o.Id, o.Title, p.Id, p.Name, o.EndDate))
            .ToListAsync(ct);
        return rows.DistinctBy(r => (r.EmployeeId, r.ModuleId)).ToList();
    }
}
