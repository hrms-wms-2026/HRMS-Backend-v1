using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;

namespace ONEVO.Application.Features.TimeAttendance.Services;

public sealed class EmployeeAttendancePeriodReader(
    IAttendanceReadRepository attendance,
    ILeaveRequestReadRepository leaveRequests,
    ILegalEntityRepository legalEntities,
    IDateTimeProvider clock) : IEmployeeAttendancePeriodReader
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

        return new AttendancePeriodData(
            records, timezone, now, clock.Today, legalEntity?.BreakDurationMinutes,
            breakMinutesByDate, leaves, rangeStart, rangeEnd,
            AttendanceScheduleResolver.WorkingWeekdays(legalEntity));
    }
}
