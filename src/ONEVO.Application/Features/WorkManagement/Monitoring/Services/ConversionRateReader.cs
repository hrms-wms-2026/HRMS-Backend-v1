using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Application.Features.TimeAttendance.Team.Services;
using ONEVO.Application.Features.WorkManagement.Planner.Services;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.WorkManagement.Monitoring.Services;

public interface IConversionRateReader
{
    /// <summary>Each employee's worked and task minutes over the forecast window [today - 28, today - 1] in the legal
    /// entity's zone (UTC and the clock's date without one). Tasks from every project count.</summary>
    Task<ConversionRateSet> ReadAsync(Guid tenantId, LegalEntity? legalEntity, IReadOnlyCollection<Guid> employeeIds,
        CancellationToken ct = default);
}

/// <summary>
/// Same sources as the Work vs task hours widget (GetTeamWorkTaskHoursQueryHandler): worked time from attendance records
/// through PlannerActuals.AttendanceMinutes (clocked time minus breaks, a still-open record counts up to that day's local
/// midnight), task time = the estimate of each top-level task the employee completed (ITeamCompletedTaskReader), dated by
/// CompletedAt in the zone. Today is never read.
/// </summary>
public sealed class ConversionRateReader(
    IAttendanceReadRepository attendance,
    ITeamCompletedTaskReader completedTasks,
    IDateTimeProvider clock) : IConversionRateReader
{
    public async Task<ConversionRateSet> ReadAsync(Guid tenantId, LegalEntity? legalEntity, IReadOnlyCollection<Guid> employeeIds,
        CancellationToken ct = default)
    {
        TimeZoneInfo zone;
        DateOnly today;
        if (legalEntity is not null)
        {
            var schedule = AttendanceScheduleResolver.Resolve(legalEntity, clock.UtcNow);
            zone = schedule.TimeZone;
            today = schedule.WorkDate;
        }
        else
        {
            zone = TimeZoneInfo.Utc;
            today = clock.Today;
        }

        var ids = employeeIds.Where(id => id != Guid.Empty).Distinct().ToList();
        var (start, end) = OvertimeForecast.Window(today);
        if (ids.Count == 0)
            return ConversionRateSet.Empty(today);

        var worked = ids.ToDictionary(id => id, _ => new int[ConversionRateSet.Weeks]);
        var task = ids.ToDictionary(id => id, _ => new int[ConversionRateSet.Weeks]);
        int Week(DateOnly date) => (date.DayNumber - start.DayNumber) / 7;

        var days = end.DayNumber - start.DayNumber + 1;
        var records = (await attendance.ListRecordsAsync(tenantId, ids, start, end, 0, Math.Max(1, ids.Count * days * 2), ct)).Items;
        foreach (var r in records)
        {
            if (r.Date < start || r.Date > end || !worked.TryGetValue(r.EmployeeId, out var buckets))
                continue;
            var asOf = AttendanceTodayStateService.GetLocalDayWindow(r.Date, zone).End;
            buckets[Week(r.Date)] += PlannerActuals.AttendanceMinutes(r.ActualStart, r.ActualEnd, r.WorkedMinutes, r.BreakMinutes, asOf);
        }

        var fromUtc = AttendanceTodayStateService.GetLocalDayWindow(start, zone).Start;
        var toUtc = AttendanceTodayStateService.GetLocalDayWindow(end, zone).End;
        foreach (var t in await completedTasks.ListCompletedAsync(tenantId, ids, fromUtc, toUtc, ct))
        {
            var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(t.CompletedAt, zone).DateTime);
            if (localDate < start || localDate > end || !task.TryGetValue(t.EmployeeId, out var buckets))
                continue;
            buckets[Week(localDate)] += TeamWorkTaskMath.EstimateMinutes(t.EstimatedHours);
        }

        var byEmployee = ids.ToDictionary(id => id, id =>
        {
            var w = worked[id].Sum();
            var k = task[id].Sum();
            return new EmployeeConversionRate(id, w, k, OvertimeForecast.Rate(k, w), worked[id], task[id]);
        });
        return new ConversionRateSet(today, start, end, byEmployee);
    }
}
