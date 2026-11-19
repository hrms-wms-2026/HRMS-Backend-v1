using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Services;

namespace ONEVO.Application.Features.TimeAttendance.Queries.EmployeeOverview.GetEmployeeAttendanceOverview;

public sealed class GetEmployeeAttendanceOverviewQueryHandler(
    IEmployeeReadAccessGuard guard,
    IEmployeeAttendancePeriodReader reader,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeAttendanceOverviewQuery, Result<EmployeeAttendanceOverviewResponse>>
{
    public async Task<Result<EmployeeAttendanceOverviewResponse>> Handle(
        GetEmployeeAttendanceOverviewQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeAttendanceOverviewResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        var period = EmployeePeriod.Resolve(request.From, request.To, clock.Today);
        if (!period.IsSuccess)
            return Result<EmployeeAttendanceOverviewResponse>.Failure(period.Error!, period.StatusCode ?? 400);

        var data = await reader.LoadAsync(tenantId, request.EmployeeId, access.Value!.LegalEntityId, period.Value!, ct);
        var c = AttendancePeriodCalculator.Classify(data);
        return Result<EmployeeAttendanceOverviewResponse>.Success(new EmployeeAttendanceOverviewResponse(
            period.Value!.From, period.Value.To, c.WorkingDays, c.Attended, c.Late, c.MissingClockOuts, c.Leave,
            c.Days, c.Absent, c.ShortHours, c.WorkedOnNonWorkingDay, c.WorkedDuringTimeOff));
    }
}
