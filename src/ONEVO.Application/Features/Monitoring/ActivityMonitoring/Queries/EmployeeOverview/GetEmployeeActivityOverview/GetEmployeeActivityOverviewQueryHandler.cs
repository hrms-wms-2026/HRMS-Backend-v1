using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.EmployeeOverview.GetEmployeeActivityOverview;

public sealed class GetEmployeeActivityOverviewQueryHandler(
    IEmployeeReadAccessGuard guard,
    IEmployeeRepository employees,
    IActivityDailySummaryRepository summaries,
    IMonitoringToggleResolver toggles,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeActivityOverviewQuery, Result<EmployeeActivityOverviewResponse>>
{
    public const string ModulePermission = "monitoring:read";

    public async Task<Result<EmployeeActivityOverviewResponse>> Handle(
        GetEmployeeActivityOverviewQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeActivityOverviewResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        if (!await EmployeeOverviewAccess.HasAccessAsync(currentUser, employees, tenantId, request.EmployeeId, ModulePermission, ct))
            return Result<EmployeeActivityOverviewResponse>.Forbidden("You do not have access to this employee's activity.");

        var compare = EmployeeOverviewCompare.Parse(request.Compare);
        if (!compare.IsSuccess)
            return Result<EmployeeActivityOverviewResponse>.Failure(compare.Error!, compare.StatusCode ?? 400);

        var period = EmployeePeriod.Resolve(request.From, request.To, clock.Today);
        if (!period.IsSuccess)
            return Result<EmployeeActivityOverviewResponse>.Failure(period.Error!, period.StatusCode ?? 400);

        var enabled = await toggles.IsEnabledAsync(tenantId, request.EmployeeId, MonitoringCapability.ActivityMonitoring, ct);
        if (!enabled)
        {
            return Result<EmployeeActivityOverviewResponse>.Success(new EmployeeActivityOverviewResponse(
                period.Value!.From, period.Value.To, false, 0, 0, 0, 0, null));
        }

        var current = await MeasureAsync(tenantId, request.EmployeeId, period.Value!, ct);
        EmployeeActivityMetrics? previous = compare.Value
            ? await MeasureAsync(tenantId, request.EmployeeId, period.Value!.Previous(), ct)
            : null;

        return Result<EmployeeActivityOverviewResponse>.Success(new EmployeeActivityOverviewResponse(
            period.Value!.From, period.Value.To, true,
            current.ActiveMinutes, current.IdleMinutes, current.MeetingMinutes, current.DaysWithData, previous));
    }

    private async Task<EmployeeActivityMetrics> MeasureAsync(
        Guid tenantId, Guid employeeId, EmployeePeriod period, CancellationToken ct)
    {
        var rows = await summaries.GetRangeAsync(tenantId, employeeId, period.From, period.To, ct);
        return new EmployeeActivityMetrics(
            rows.Sum(r => r.TotalActiveMinutes),
            rows.Sum(r => r.TotalIdleMinutes),
            rows.Sum(r => r.TotalMeetingMinutes),
            rows.Count(r => r.TotalActiveMinutes + r.TotalIdleMinutes + r.TotalMeetingMinutes > 0));
    }
}
