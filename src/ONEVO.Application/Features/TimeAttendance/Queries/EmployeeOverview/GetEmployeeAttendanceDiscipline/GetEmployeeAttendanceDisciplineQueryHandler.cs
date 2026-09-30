using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.Monitoring.Notifications.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Queries.EmployeeOverview.GetEmployeeAttendanceDiscipline;

public sealed class GetEmployeeAttendanceDisciplineQueryHandler(
    IEmployeeReadAccessGuard guard,
    IEmployeeRepository employees,
    IEmployeeAttendancePeriodReader reader,
    IMonitoringToggleResolver toggles,
    INotificationRepository notifications,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeAttendanceDisciplineQuery, Result<EmployeeAttendanceDisciplineResponse>>
{
    public const string ModulePermission = "attendance:read";

    public async Task<Result<EmployeeAttendanceDisciplineResponse>> Handle(
        GetEmployeeAttendanceDisciplineQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeAttendanceDisciplineResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        if (!await EmployeeOverviewAccess.HasAccessAsync(currentUser, employees, tenantId, request.EmployeeId, ModulePermission, ct))
            return Result<EmployeeAttendanceDisciplineResponse>.Forbidden("You do not have access to this employee's attendance.");

        var period = EmployeePeriod.Resolve(request.From, request.To, clock.Today);
        if (!period.IsSuccess)
            return Result<EmployeeAttendanceDisciplineResponse>.Failure(period.Error!, period.StatusCode ?? 400);

        var data = await reader.LoadAsync(tenantId, request.EmployeeId, access.Value!.LegalEntityId, period.Value!, ct);
        var counts = AttendancePeriodCalculator.Count(data.Records, data.Timezone, data.Now);

        var overBreakDays = 0;
        var overBreakMinutes = 0;
        if (data.BreakAllowanceMinutes is int allowance)
        {
            foreach (var record in data.Records)
            {
                if (data.BreakMinutesByDate.TryGetValue(record.Date, out var used) && used > allowance)
                {
                    overBreakDays += 1;
                    overBreakMinutes += used - allowance;
                }
            }
        }

        var trackingEnabled = await toggles.IsEnabledAsync(
            tenantId, request.EmployeeId, MonitoringCapability.WorkLocationVerification, ct);
        int? locationViolations = trackingEnabled
            ? await notifications.CountByTypeAsync(
                tenantId, request.EmployeeId, NotificationType.OutsideWorkLocationAlert,
                data.RangeStartUtc, data.RangeEndUtc, ct)
            : null;

        return Result<EmployeeAttendanceDisciplineResponse>.Success(new EmployeeAttendanceDisciplineResponse(
            period.Value!.From,
            period.Value.To,
            counts.LateArrivals,
            counts.EarlyDepartures,
            counts.MissingClockOuts,
            overBreakDays,
            overBreakMinutes,
            trackingEnabled,
            locationViolations));
    }
}
