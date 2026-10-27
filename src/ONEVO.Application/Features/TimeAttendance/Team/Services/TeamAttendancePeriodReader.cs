using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Calendar.Services;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Alerts;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Team.Services;

/// <summary>
/// Classifies every covered employee's days with the unchanged AttendancePeriodCalculator.Classify, each in their own
/// legal entity's timezone and working week. Excused (or converted-to-leave) late and absent days drop exactly as on
/// the employee Overview: an excused late day is Present, an excused absence is not counted. Leave the viewer may not
/// see (Team Status's leave scope) counts as Absent. Breaks aren't loaded: no bucket depends on them.
/// </summary>
public sealed class TeamAttendancePeriodReader(
    IAttendanceReadRepository attendance,
    ILeaveRequestReadRepository leaveRequests,
    ILegalEntityRepository legalEntities,
    IDateTimeProvider clock,
    IEmployeeRepository employees,
    ICalendarEventRepository calendarEvents,
    IPositionAssignmentRepository positionAssignments,
    IAttendanceAlertResolutionLookup? alertResolutions = null,
    IEmployeeClockInPolicyResolver? policies = null,
    ILeaveVisibilityScopeProvider? leaveVisibility = null) : ITeamAttendancePeriodReader
{
    public async Task<IReadOnlyList<TeamAttendanceEmployeeDays>> LoadAsync(
        Guid tenantId, IReadOnlyCollection<Guid> employeeIds, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
            return [];

        var ids = employeeIds.Distinct().ToList();
        var dayCount = to.DayNumber - from.DayNumber + 1;
        var (records, _) = await attendance.ListRecordsAsync(tenantId, ids, from, to, 0, ids.Count * (dayCount + 1), ct);
        var recordsByEmployee = records.GroupBy(r => r.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());
        var leaves = await leaveRequests.ListApprovedCoveringAsync(tenantId, ids, from, to, ct);
        var leavesByEmployee = leaves.GroupBy(l => l.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());
        var holidays = (await calendarEvents.ListHolidayDatesAsync(tenantId, from, to, ct)).ToHashSet();
        var employeesById = await employees.ListByIdsAsync(tenantId, ids, ct);
        var excused = alertResolutions is null
            ? new Dictionary<Guid, IReadOnlyDictionary<string, IReadOnlySet<DateOnly>>>()
            : await alertResolutions.ExcusedDatesForEmployeesAsync(tenantId, ids, from, to, ct);

        var entities = new Dictionary<Guid, LegalEntity?>();
        var thresholds = new Dictionary<Guid, int>();
        foreach (var group in employeesById.Values.Where(e => e.LegalEntityId is not null).GroupBy(e => e.LegalEntityId!.Value))
        {
            entities[group.Key] = await legalEntities.GetByIdForTenantAsync(tenantId, group.Key, ct);
            if (policies is null) continue;
            foreach (var (employeeId, minutes) in await policies.ResolveThresholdsAsync(tenantId, group.Key, group.ToList(), to, ct))
                thresholds[employeeId] = minutes;
        }

        var leaveScope = leaveVisibility is null ? null : (await leaveVisibility.ResolveForCurrentUserAsync(ct)).Scope;
        var seats = leaveScope is null
            ? new Dictionary<Guid, PositionAssignment>()
            : await positionAssignments.GetActivePrimaryByEmployeeIdsAsync(tenantId, ids, ct);

        var now = clock.UtcNow;
        var result = new List<TeamAttendanceEmployeeDays>(ids.Count);
        foreach (var id in ids)
        {
            if (!employeesById.TryGetValue(id, out var employee))
                continue;

            var entity = employee.LegalEntityId is Guid entityId ? entities.GetValueOrDefault(entityId) : null;
            var employeeRecords = recordsByEmployee.GetValueOrDefault(id) ?? [];
            var timezone = AttendancePeriodCalculator.ResolveTimezone(entity?.Timezone ?? employeeRecords.FirstOrDefault()?.ScheduleTimezone);
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timezone).DateTime);
            // Same rule as EmployeeAttendancePeriodReader: no configured schedule means no expected days.
            IReadOnlySet<int> weekdays = entity is not null && AttendanceScheduleResolver.IsScheduleConfigured(entity)
                ? AttendanceScheduleResolver.ParseWorkingDays(entity.StandardWorkingDays)
                : new HashSet<int>();
            var workdays = ExpectedWorkdayCalendar.Build(weekdays, holidays, from, to, employee.HireDate, employee.TerminationDate, today);

            var data = new AttendancePeriodData(
                employeeRecords, timezone, now, today, entity?.BreakDurationMinutes, new Dictionary<DateOnly, int>(),
                leavesByEmployee.GetValueOrDefault(id) ?? [],
                AttendanceTodayStateService.GetLocalDayWindow(from, timezone).Start,
                AttendanceTodayStateService.GetLocalDayWindow(to, timezone).End,
                workdays, excused.GetValueOrDefault(id), thresholds.GetValueOrDefault(id));
            var classification = AttendancePeriodCalculator.Classify(data);

            var late = classification.Dates.Late.ToHashSet();
            var absent = classification.Dates.Absent.ToHashSet();
            var leaveAuthorized = leaveScope is not null && EmployeeVisibilityScopeMatcher.Includes(
                leaveScope, id, seats.GetValueOrDefault(id)?.PositionId, employee.DepartmentId, employee.LegalEntityId);

            var buckets = new Dictionary<DateOnly, string>(classification.Days.Count);
            foreach (var day in classification.Days)
            {
                // Classify's day list keeps excused days as they happened; its Late/Absent lists drop them.
                var status = day.Status switch
                {
                    "late" when !late.Contains(day.Date) => "present",
                    "absent" when !absent.Contains(day.Date) => "off",
                    _ => day.Status,
                };
                buckets[day.Date] = TeamAttendanceMath.Bucket(status, late.Contains(day.Date), leaveAuthorized);
            }

            result.Add(new TeamAttendanceEmployeeDays(id, employee.DepartmentId, buckets));
        }

        return result;
    }
}
