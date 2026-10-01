using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Services;

namespace ONEVO.Application.Features.TimeAttendance.Queries.EmployeeOverview.GetEmployeeAttendanceOverview;

public sealed class GetEmployeeAttendanceOverviewQueryHandler(
    IEmployeeReadAccessGuard guard,
    IEmployeeRepository employees,
    IEmployeeAttendancePeriodReader reader,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeAttendanceOverviewQuery, Result<EmployeeAttendanceOverviewResponse>>
{
    public const string ModulePermission = "attendance:read";

    public async Task<Result<EmployeeAttendanceOverviewResponse>> Handle(
        GetEmployeeAttendanceOverviewQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeAttendanceOverviewResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        if (!await EmployeeOverviewAccess.HasAccessAsync(currentUser, employees, tenantId, request.EmployeeId, ModulePermission, ct))
            return Result<EmployeeAttendanceOverviewResponse>.Forbidden("You do not have access to this employee's attendance.");

        var period = EmployeePeriod.Resolve(request.From, request.To, clock.Today);
        if (!period.IsSuccess)
            return Result<EmployeeAttendanceOverviewResponse>.Failure(period.Error!, period.StatusCode ?? 400);

        var data = await reader.LoadAsync(tenantId, request.EmployeeId, access.Value!.LegalEntityId, period.Value!, ct);
        var counts = AttendancePeriodCalculator.Count(data.Records, data.Timezone, data.Now);

        // One entry per calendar day in the period, not per stored row: days nobody clocked in on
        // have no attendance_record, and must still show as absent / off / upcoming.
        var recordsByDate = data.Records.GroupBy(r => r.Date).ToDictionary(g => g.Key, g => g.First());
        var workingWeekdays = data.WorkingWeekdays ?? AttendanceScheduleResolver.WorkingWeekdays(null);
        var days = new List<EmployeeAttendanceDay>();
        var workingDays = 0;

        for (var date = period.Value!.From; date <= period.Value.To; date = date.AddDays(1))
        {
            var hasLeave = data.ApprovedLeaves.Any(l => AttendancePeriodCalculator.CoversDate(l, date));
            recordsByDate.TryGetValue(date, out var record);
            var isWorkingDay = record is not null
                ? record.ExpectedWorkingDay && !record.IsHoliday
                : workingWeekdays.Contains(AttendanceScheduleResolver.ToIsoDay(date));

            // "X of Y working days attended" only counts days that have already started.
            if (isWorkingDay && date <= data.Today)
                workingDays += 1;

            var status = record is not null
                ? AttendancePeriodCalculator.DayStatus(record, data.Timezone, data.Now, data.Today, hasLeave)
                : AttendancePeriodCalculator.DayStatusWithoutRecord(date, isWorkingDay, data.Today, hasLeave);
            days.Add(new EmployeeAttendanceDay(date, status));
        }

        return Result<EmployeeAttendanceOverviewResponse>.Success(new EmployeeAttendanceOverviewResponse(
            period.Value.From,
            period.Value.To,
            workingDays,
            counts.DaysPresent,
            counts.LateArrivals,
            counts.MissingClockOuts,
            days.Count(d => d.Status == "leave"),
            days));
    }
}
