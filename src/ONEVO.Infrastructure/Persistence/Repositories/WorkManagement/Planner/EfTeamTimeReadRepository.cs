using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.WorkManagement.Planner.RepositoryInterfaces;
using ONEVO.Domain.Lookups;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Planner;

/// <summary>Batched block and clocking reads for the My Team widgets. Task joins keep the default query filters, so meeting
/// tasks never appear.</summary>
public sealed class EfTeamTimeReadRepository(ApplicationDbContext db) : ITeamTimeReadRepository
{
    public async Task<IReadOnlyList<TeamBlockRow>> ListBlocksForTasksAsync(Guid tenantId, IReadOnlyCollection<Guid> objectiveIds,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        if (objectiveIds.Count == 0)
            return [];
        var (fromUtc, toUtc) = (from.ToUniversalTime(), to.ToUniversalTime());
        return await (
            from b in db.PlannedTaskBlocks.AsNoTracking()
            join t in db.WorkTasks.AsNoTracking() on b.TaskId equals t.Id
            where b.TenantId == tenantId && t.TenantId == tenantId && !b.IsDeleted && !t.IsDeleted
                  && objectiveIds.Contains(t.ObjectiveId) && b.StartAt < toUtc && b.EndAt > fromUtc
            select new TeamBlockRow(b.EmployeeId, b.TaskId, b.StartAt, b.EndAt))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TeamSessionRow>> ListSessionsForTasksAsync(Guid tenantId, IReadOnlyCollection<Guid> objectiveIds,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        if (objectiveIds.Count == 0)
            return [];
        var (fromUtc, toUtc) = (from.ToUniversalTime(), to.ToUniversalTime());
        return await (
            from s in db.TaskClockingSessions.AsNoTracking()
            join t in db.WorkTasks.AsNoTracking() on s.TaskId equals t.Id
            where s.TenantId == tenantId && t.TenantId == tenantId && !s.IsDeleted && !t.IsDeleted
                  && objectiveIds.Contains(t.ObjectiveId)
                  && s.ClockInAt < toUtc && (s.ClockOutAt == null || s.ClockOutAt > fromUtc)
            select new TeamSessionRow(s.EmployeeId, s.TaskId, s.ClockInAt, s.ClockOutAt))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TeamBlockRow>> ListBlocksForEmployeesAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
            return [];
        var (fromUtc, toUtc) = (from.ToUniversalTime(), to.ToUniversalTime());
        return await (
            from b in db.PlannedTaskBlocks.AsNoTracking()
            join t in db.WorkTasks.AsNoTracking() on b.TaskId equals t.Id
            where b.TenantId == tenantId && !b.IsDeleted && !t.IsDeleted && employeeIds.Contains(b.EmployeeId)
                  && b.StartAt < toUtc && b.EndAt > fromUtc
            orderby b.StartAt
            select new TeamBlockRow(b.EmployeeId, b.TaskId, b.StartAt, b.EndAt))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TeamSessionRow>> ListSessionsForEmployeesAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
            return [];
        var (fromUtc, toUtc) = (from.ToUniversalTime(), to.ToUniversalTime());
        return await (
            from s in db.TaskClockingSessions.AsNoTracking()
            join t in db.WorkTasks.AsNoTracking() on s.TaskId equals t.Id
            where s.TenantId == tenantId && !s.IsDeleted && !t.IsDeleted && employeeIds.Contains(s.EmployeeId)
                  && s.ClockInAt < toUtc && (s.ClockOutAt == null || s.ClockOutAt > fromUtc)
            select new TeamSessionRow(s.EmployeeId, s.TaskId, s.ClockInAt, s.ClockOutAt))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, TeamTaskRow>> GetTasksAsync(Guid tenantId, IReadOnlyCollection<Guid> taskIds,
        CancellationToken ct = default)
    {
        if (taskIds.Count == 0)
            return new Dictionary<Guid, TeamTaskRow>();
        var rows = await (
            from t in db.WorkTasks.AsNoTracking()
            join st in db.TaskStatuses.AsNoTracking() on t.StatusId equals st.Id into statuses
            from st in statuses.DefaultIfEmpty()
            where t.TenantId == tenantId && !t.IsDeleted && taskIds.Contains(t.Id)
            select new
            {
                t.Id, t.ShortId, t.Title, t.ObjectiveId, t.ProjectId, t.ProgressPercent,
                MarksComplete = st != null && st.MarksTaskComplete,
            })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => new TeamTaskRow(r.Id, r.ShortId, r.Title, r.ObjectiveId, r.ProjectId,
            r.MarksComplete || r.ProgressPercent >= 100, r.ProgressPercent));
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetTimezonesAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds,
        CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
            return new Dictionary<Guid, string>();
        var rows = await (
            from e in db.Employees.AsNoTracking()
            join le in db.LegalEntities.AsNoTracking() on e.LegalEntityId equals le.Id into entities
            from le in entities.DefaultIfEmpty()
            where e.TenantId == tenantId && employeeIds.Contains(e.Id)
            select new { e.Id, e.DisplayTimezone, LegalEntityTimezone = le == null ? null : le.Timezone })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r =>
            !string.IsNullOrWhiteSpace(r.DisplayTimezone) ? r.DisplayTimezone!
            : !string.IsNullOrWhiteSpace(r.LegalEntityTimezone) ? r.LegalEntityTimezone!
            : "UTC");
    }

    public async Task<IReadOnlyDictionary<Guid, TeamPersonRow>> GetPeopleAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds,
        CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
            return new Dictionary<Guid, TeamPersonRow>();
        var avatars = db.EntityAssets.AsNoTracking().Where(a => a.TenantId == tenantId
            && a.OwnerType == EntityAssetOwnerTypes.Employee && a.AssetPurpose == UploadPurposeCatalog.EmployeeAvatar && a.IsPrimary);
        var rows = await (
            from e in db.Employees.AsNoTracking()
            join asset in avatars on e.Id equals asset.OwnerId into assetJoin
            from asset in assetJoin.DefaultIfEmpty()
            where e.TenantId == tenantId && employeeIds.Contains(e.Id)
            select new
            {
                e.Id, e.FirstName, e.LastName, e.EmploymentStatusId,
                AvatarFileId = asset == null ? (Guid?)null : asset.FileRecordId,
            })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.Id).ToDictionary(g => g.Key, g =>
        {
            var r = g.First();
            return new TeamPersonRow(r.Id, $"{r.FirstName} {r.LastName}".Trim(), r.AvatarFileId,
                r.EmploymentStatusId == EmploymentStatusIds.Active);
        });
    }
}
