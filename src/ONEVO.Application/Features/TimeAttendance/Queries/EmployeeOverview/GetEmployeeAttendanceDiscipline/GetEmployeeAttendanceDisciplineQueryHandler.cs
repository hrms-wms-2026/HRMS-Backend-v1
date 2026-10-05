using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.Monitoring.Notifications.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Queries.EmployeeOverview.GetEmployeeAttendanceDiscipline;

public sealed class GetEmployeeAttendanceDisciplineQueryHandler(
    IEmployeeReadAccessGuard guard,
    IEmployeeAttendancePeriodReader reader,
    IMonitoringToggleResolver toggles,
    INotificationRepository notifications,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeAttendanceDisciplineQuery, Result<EmployeeAttendanceDisciplineResponse>>
{
    public async Task<Result<EmployeeAttendanceDisciplineResponse>> Handle(
        GetEmployeeAttendanceDisciplineQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeAttendanceDisciplineResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        var compare = EmployeeOverviewCompare.Parse(request.Compare);
        if (!compare.IsSuccess)
            return Result<EmployeeAttendanceDisciplineResponse>.Failure(compare.Error!, compare.StatusCode ?? 400);

        var period = EmployeePeriod.Resolve(request.From, request.To, clock.Today);
        if (!period.IsSuccess)
            return Result<EmployeeAttendanceDisciplineResponse>.Failure(period.Error!, period.StatusCode ?? 400);

        var legalEntityId = access.Value!.LegalEntityId;
        var trackingEnabled = await toggles.IsEnabledAsync(
            tenantId, request.EmployeeId, MonitoringCapability.WorkLocationVerification, ct);

        var current = await MeasureAsync(tenantId, request.EmployeeId, legalEntityId, period.Value!, trackingEnabled, ct);
        EmployeeAttendanceDisciplineMetrics? previous = compare.Value
            ? await MeasureAsync(tenantId, request.EmployeeId, legalEntityId, period.Value!.Previous(), trackingEnabled, ct)
            : null;

        return Result<EmployeeAttendanceDisciplineResponse>.Success(new EmployeeAttendanceDisciplineResponse(
            period.Value!.From,
            period.Value.To,
            current.LateClockIns,
            current.EarlyClockOuts,
            current.MissingClockOuts,
            current.OverBreakDays,
            current.OverBreakMinutes,
            trackingEnabled,
            current.LocationViolations,
            previous));
    }

    private async Task<EmployeeAttendanceDisciplineMetrics> MeasureAsync(
        Guid tenantId, Guid employeeId, Guid? legalEntityId, EmployeePeriod period, bool trackingEnabled, CancellationToken ct)
    {
        var data = await reader.LoadAsync(tenantId, employeeId, legalEntityId, period, ct);
        var counts = AttendancePeriodCalculator.Classify(data);

        var overBreak = AttendancePeriodCalculator.OverBreakDays(data);
        var overBreakDays = overBreak.Count;
        var overBreakMinutes = overBreak.Sum(o => o.MinutesOver);

        int? locationViolations = trackingEnabled
            ? await notifications.CountByTypeAsync(
                tenantId, employeeId, NotificationType.OutsideWorkLocationAlert,
                data.RangeStartUtc, data.RangeEndUtc, ct)
            : null;

        return new EmployeeAttendanceDisciplineMetrics(
            counts.Late, counts.EarlyDepartures, counts.MissingClockOuts,
            overBreakDays, overBreakMinutes, locationViolations);
    }
}
