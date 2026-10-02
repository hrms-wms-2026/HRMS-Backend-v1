using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.EmployeeOverview.GetEmployeeActivityOverview;

public sealed class GetEmployeeActivityOverviewQueryHandler(
    IEmployeeReadAccessGuard guard,
    IActivityDailySummaryRepository summaries,
    IActivityLiveDaySummary live,
    IMonitoringToggleResolver toggles,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeActivityOverviewQuery, Result<EmployeeActivityOverviewResponse>>
{
    public async Task<Result<EmployeeActivityOverviewResponse>> Handle(
        GetEmployeeActivityOverviewQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeActivityOverviewResponse>.Failure(access.Error!, access.StatusCode ?? 400);

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
        var rows = (await summaries.GetRangeAsync(tenantId, employeeId, period.From, period.To, ct))
            .Select(r => (r.Date, Active: r.TotalActiveMinutes, Idle: r.TotalIdleMinutes, Meeting: r.TotalMeetingMinutes))
            .ToList();

        // Today has no persisted summary until the nightly job runs; compose it live (same source the
        // attendance day-detail uses) so this card agrees with the hourly chart and app usage.
        var today = clock.Today;
        if (period.From <= today && today <= period.To && rows.All(r => r.Date != today))
        {
            var liveDay = await live.ComposeAsync(tenantId, employeeId, today, ct);
            if (liveDay is not null)
                rows.Add((today, liveDay.TotalActiveMinutes, liveDay.TotalIdleMinutes, liveDay.TotalMeetingMinutes));
        }

        return new EmployeeActivityMetrics(
            rows.Sum(r => r.Active),
            rows.Sum(r => r.Idle),
            rows.Sum(r => r.Meeting),
            rows.Count(r => r.Active + r.Idle + r.Meeting > 0));
    }
}
