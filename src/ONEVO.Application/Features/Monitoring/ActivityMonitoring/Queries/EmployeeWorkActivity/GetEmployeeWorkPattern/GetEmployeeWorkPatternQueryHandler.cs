using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Services;
using ONEVO.Application.Features.Monitoring.AppUsage.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.EmployeeWorkActivity.GetEmployeeWorkPattern;

/// <summary>Focus blocks (WorkPatternWindowClassifier per UTC day, summed), top app by minutes and
/// the longest contiguous idle stretch in the period.</summary>
public sealed class GetEmployeeWorkPatternQueryHandler(
    IEmployeeReadAccessGuard guard,
    IActivitySnapshotRepository snapshots,
    IMeetingSignalRepository meetings,
    IAppUsageSnapshotRepository apps,
    IMonitoringToggleResolver toggles,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeWorkPatternQuery, Result<EmployeeWorkPatternSummaryResponse>>
{
    public async Task<Result<EmployeeWorkPatternSummaryResponse>> Handle(GetEmployeeWorkPatternQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeWorkPatternSummaryResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        var period = EmployeePeriod.Resolve(request.From, request.To, clock.Today);
        if (!period.IsSuccess)
            return Result<EmployeeWorkPatternSummaryResponse>.Failure(period.Error!, period.StatusCode ?? 400);
        var p = period.Value!;
        if (EmployeeWorkActivityCalculator.ExceedsRawRange(p.From, p.To))
            return Result<EmployeeWorkPatternSummaryResponse>.Failure($"The period cannot exceed {EmployeeWorkActivityCalculator.MaxRawDays} days.");

        if (!await toggles.IsEnabledAsync(tenantId, request.EmployeeId, MonitoringCapability.ActivityMonitoring, ct))
            return Result<EmployeeWorkPatternSummaryResponse>.Success(new EmployeeWorkPatternSummaryResponse(p.From, p.To, false, 0, null, 0, null));

        var (from, to) = EmployeeWorkActivityCalculator.UtcWindow(p.From, p.To);
        var windows = await snapshots.GetWindowsByEmployeeRangeAsync(tenantId, request.EmployeeId, from, to, ct);
        var signals = await meetings.GetByEmployeeRangeAsync(tenantId, request.EmployeeId, from, to, ct);

        // Same per-UTC-day classification the nightly job and my-work-pattern use.
        var signalsByDay = signals.ToLookup(s => DateOnly.FromDateTime(s.CapturedAt.UtcDateTime));
        var focusBlocks = windows
            .GroupBy(w => DateOnly.FromDateTime(w.CapturedAt.UtcDateTime))
            .Sum(day => WorkPatternWindowClassifier.Classify(day.ToList(), signalsByDay[day.Key].ToList()).FocusBlocks);

        string? topApp = null;
        var topAppMinutes = 0;
        if (await toggles.IsEnabledAsync(tenantId, request.EmployeeId, MonitoringCapability.ApplicationTracking, ct))
        {
            var totals = await apps.GetMinutesByProcessAsync(tenantId, request.EmployeeId, from, to, ct);
            var top = totals.OrderByDescending(t => t.Samples).ThenBy(t => t.ProcessName).FirstOrDefault();
            topApp = top?.ProcessName;
            topAppMinutes = top?.Samples ?? 0;
        }

        return Result<EmployeeWorkPatternSummaryResponse>.Success(new EmployeeWorkPatternSummaryResponse(
            p.From, p.To, true, focusBlocks, topApp, topAppMinutes, EmployeeWorkActivityCalculator.LongestIdle(windows)));
    }
}
