using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Services;

namespace ONEVO.Application.Features.TimeAttendance.Queries.EmployeeOverview.GetEmployeeAttendanceDays;

/// <summary>
/// The day-by-day list behind the Overview Attendance card. Same period data and rules as
/// GetEmployeeAttendanceOverviewQueryHandler, so each day's status matches the card's strip.
/// Loaded only when the card is opened.
/// </summary>
public sealed class GetEmployeeAttendanceDaysQueryHandler(
    IEmployeeReadAccessGuard guard,
    IEmployeeAttendancePeriodReader reader,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeAttendanceDaysQuery, Result<EmployeeAttendanceDaysResponse>>
{
    public async Task<Result<EmployeeAttendanceDaysResponse>> Handle(GetEmployeeAttendanceDaysQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeAttendanceDaysResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        var period = EmployeePeriod.Resolve(request.From, request.To, clock.Today);
        if (!period.IsSuccess)
            return Result<EmployeeAttendanceDaysResponse>.Failure(period.Error!, period.StatusCode ?? 400);

        var data = await reader.LoadAsync(tenantId, request.EmployeeId, access.Value!.LegalEntityId, period.Value!, ct);
        return Result<EmployeeAttendanceDaysResponse>.Success(new EmployeeAttendanceDaysResponse(
            period.Value!.From, period.Value.To, data.Timezone.Id, AttendancePeriodCalculator.DayDetails(data)));
    }
}
