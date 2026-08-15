using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.GetActivityDailySummary;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Services;
using ONEVO.Application.Features.Monitoring.Dashboard.DTOs;
using ONEVO.Application.Features.Monitoring.Dashboard.Services;
using ONEVO.Application.Features.Monitoring.DeviceState.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.WorkSessions.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.WorkSessions.Entities;

namespace ONEVO.Application.Features.Monitoring.Dashboard.Queries.GetMonitoringDashboard;

public sealed class GetMonitoringDashboardQueryHandler
    : IRequestHandler<GetMonitoringDashboardQuery, Result<MonitoringDashboardDto>>
{
    private readonly IEmployeeRepository _employees;
    private readonly IEmployeeVisibilityScopeResolver _visibilityScopeResolver;
    private readonly ICurrentUser _currentUser;
    private readonly IActivityDailySummaryRepository _summaries;
    private readonly IDeviceStateSnapshotRepository _deviceStates;
    private readonly IWorkSessionRepository _workSessions;
    private readonly IMonitoringReportTimeZoneResolver _timeZoneResolver;
    private readonly IDateTimeProvider _clock;

    public GetMonitoringDashboardQueryHandler(
        IEmployeeRepository employees,
        IEmployeeVisibilityScopeResolver visibilityScopeResolver,
        ICurrentUser currentUser,
        IActivityDailySummaryRepository summaries,
        IDeviceStateSnapshotRepository deviceStates,
        IWorkSessionRepository workSessions,
        IMonitoringReportTimeZoneResolver timeZoneResolver,
        IDateTimeProvider clock)
    {
        _employees = employees;
        _visibilityScopeResolver = visibilityScopeResolver;
        _currentUser = currentUser;
        _summaries = summaries;
        _deviceStates = deviceStates;
        _workSessions = workSessions;
        _timeZoneResolver = timeZoneResolver;
        _clock = clock;
    }

    public async Task<Result<MonitoringDashboardDto>> Handle(
        GetMonitoringDashboardQuery request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.TenantId == Guid.Empty)
            return Result<MonitoringDashboardDto>.Failure("Tenant context is required.", 401);

        var tenantId = _currentUser.TenantId;
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var scope = _currentUser.HasPermission("org:manage")
            ? EmployeeVisibilityScope.Unrestricted()
            : await _visibilityScopeResolver.ResolveAsync(tenantId, _currentUser.UserId, cancellationToken);

        var (visibleEmployees, totalCount) = await _employees.ListVisibleAsync(
            tenantId,
            scope,
            new EmployeeListFilter(request.Search, request.DepartmentId, request.LegalEntityId),
            page,
            pageSize,
            cancellationToken);

        var employeeIds = visibleEmployees.Select(e => e.Id).ToList();
        var latestDeviceStates = await _deviceStates.GetLatestForEmployeesAsync(
            tenantId,
            employeeIds,
            cancellationToken);

        var items = new List<MonitoringEmployeeDashboardItemDto>(visibleEmployees.Count);
        foreach (var employee in visibleEmployees)
        {
            var summaryEntity = await _summaries.GetAsync(
                tenantId,
                employee.Id,
                request.Date,
                cancellationToken);

            var activity = summaryEntity is null
                ? null
                : GetActivityDailySummaryQueryHandler.Map(summaryEntity);

            var workSessionReport = await GetWorkSessionReportAsync(
                tenantId,
                employee.Id,
                request.Date,
                cancellationToken);

            latestDeviceStates.TryGetValue(employee.Id, out var latestState);
            var status = MonitoringDashboardStatusService.ResolveStatus(
                latestState?.CapturedAt,
                latestState?.IsIdle,
                _clock.UtcNow);

            var alerts = MonitoringAlertEvaluator.Evaluate(
                activity,
                workSessionReport.Sessions,
                workSessionReport.TimeZone);

            items.Add(new MonitoringEmployeeDashboardItemDto(
                EmployeeId: employee.Id,
                EmployeeNumber: employee.EmployeeNumber,
                FullName: employee.FullName,
                Email: employee.Email,
                DepartmentName: employee.DepartmentName,
                PositionName: employee.PositionName,
                Status: status,
                LastCapturedAt: latestState?.CapturedAt,
                ActiveMinutes: activity?.TotalActiveMinutes ?? 0,
                IdleMinutes: activity?.TotalIdleMinutes ?? 0,
                ActivityScore: activity?.ActivityScore,
                DataCoveragePercentage: activity?.DataCoveragePercentage,
                TopApps: activity?.TopApps ?? [],
                Alerts: alerts));
        }

        return Result<MonitoringDashboardDto>.Success(new MonitoringDashboardDto(
            Date: request.Date,
            Summary: MonitoringDashboardStatusService.Summarize(items),
            Employees: items,
            TotalCount: totalCount,
            Page: page,
            PageSize: pageSize));
    }

    private async Task<WorkSessionReport> GetWorkSessionReportAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly date,
        CancellationToken ct)
    {
        var timeZone = await _timeZoneResolver.ResolveAsync(tenantId, employeeId, ct);
        var (fromUtc, toUtc) = MonitoringReportDateRange.ToUtcBounds(date, timeZone);

        var sessions = await _workSessions.GetByEmployeeRangeAsync(
            tenantId,
            employeeId,
            fromUtc,
            toUtc,
            ct);

        return new WorkSessionReport(
            sessions.Select(MapWorkSession).ToList(),
            timeZone);
    }

    private static WorkSessionReportDto MapWorkSession(EmployeeWorkSession session)
        => new(
            SessionId: session.Id,
            ClockInAt: session.ClockInAt,
            ClockOutAt: session.ClockOutAt,
            WorkSeconds: session.AccumulatedWorkSeconds,
            BreakSeconds: session.AccumulatedBreakSeconds,
            BreakCount: session.BreakSessionCount);

    private sealed record WorkSessionReport(
        IReadOnlyList<WorkSessionReportDto> Sessions,
        TimeZoneInfo TimeZone);
}
