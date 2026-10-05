using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;

namespace ONEVO.Application.Features.TimeAttendance.Services;

public sealed class EmployeeAttendancePeriodReader(
    IAttendanceReadRepository attendance,
    ILeaveRequestReadRepository leaveRequests,
    ILegalEntityRepository legalEntities,
    IDateTimeProvider clock,
    IEmployeeRepository employees,
    ICalendarEventRepository calendarEvents) : IEmployeeAttendancePeriodReader
{
    // A 366-day period has at most 366 attendance rows.
    private const int MaxRecords = 400;

    public async Task<AttendancePeriodData> LoadAsync(
        Guid tenantId, Guid employeeId, Guid? legalEntityId, EmployeePeriod period, CancellationToken ct = default)
    {
        var (records, _) = await attendance.ListRecordsAsync(
            tenantId, [employeeId], period.From, period.To, 0, MaxRecords, ct);

        var legalEntity = legalEntityId is Guid entityId
            ? await legalEntities.GetByIdForTenantAsync(tenantId, entityId, ct)
            : null;
        var timezone = AttendancePeriodCalculator.ResolveTimezone(
            legalEntity?.Timezone ?? records.FirstOrDefault()?.ScheduleTimezone);
        var now = clock.UtcNow;

        var rangeStart = AttendanceTodayStateService.GetLocalDayWindow(period.From, timezone).Start;
        var rangeEnd = AttendanceTodayStateService.GetLocalDayWindow(period.To, timezone).End;
        var breaks = await attendance.ListBreaksAsync(tenantId, employeeId, rangeStart, rangeEnd, ct);

        // Same rule the history rows use: computed from break records when any exist, otherwise
        // the persisted per-day BreakMinutes.
        var breakMinutesByDate = new Dictionary<DateOnly, int>();
        foreach (var record in records)
        {
            var window = AttendanceTodayStateService.GetLocalDayWindow(record.Date, timezone);
            breakMinutesByDate[record.Date] = breaks.Count > 0
                ? AttendanceTodayStateService.CalculateBreakUsage(breaks, window, now)
                : record.BreakMinutes;
        }

        var leaves = await leaveRequests.ListApprovedCoveringAsync(tenantId, [employeeId], period.From, period.To, ct);

        var employee = await employees.GetByIdAsync(tenantId, employeeId, ct);
        var holidays = (await calendarEvents.ListHolidayDatesAsync(tenantId, period.From, period.To, ct)).ToHashSet();
        // No legal entity, or General settings without working hours/timezone: clock-in treats every
        // day as non-working, so nothing is "expected" - otherwise every such employee floods with absences.
        IReadOnlySet<int> weekdays = legalEntity is not null && AttendanceScheduleResolver.IsScheduleConfigured(legalEntity)
            ? AttendanceScheduleResolver.ParseWorkingDays(legalEntity.StandardWorkingDays)
            : new HashSet<int>();
        var workdays = ExpectedWorkdayCalendar.Build(
            weekdays, holidays, period.From, period.To,
            employee?.HireDate ?? period.From, employee?.TerminationDate, clock.Today);

        return new AttendancePeriodData(
            records, timezone, now, clock.Today, legalEntity?.BreakDurationMinutes,
            breakMinutesByDate, leaves, rangeStart, rangeEnd, workdays);
    }
}
