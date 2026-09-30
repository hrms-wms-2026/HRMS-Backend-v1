using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeApprovalActivity;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Queries.EmployeeOverview.GetEmployeeAttendanceDiscipline;
using ONEVO.Application.Features.TimeAttendance.Queries.EmployeeOverview.GetEmployeeAttendanceOverview;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.GetEmployeeWorkOverview;
using ONEVO.Domain.Features.Monitoring.Notifications.Entities;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeOverviewSignals;

/// <summary>
/// Every active violation for one employee in from..to, ordered by importance rank
/// (<see cref="EmployeeSignalCatalogue"/>). Re-uses the per-widget overview queries, which apply
/// their own access checks. The two gates the controller would otherwise apply are rebuilt here:
/// the Work Management modules for overdue tasks, and the exception-alert scope for the
/// exceptions count. Each source loads independently, so one failing source omits only its
/// signals. Monitoring counts use UTC day bounds, which is close enough for alert counts.
/// </summary>
public sealed class GetEmployeeOverviewSignalsQueryHandler(
    IEmployeeReadAccessGuard guard,
    ISender sender,
    IModuleEntitlementService modules,
    IMonitoringToggleResolver toggles,
    INotificationRepository notifications,
    IExceptionRepository exceptions,
    IExceptionScopeResolver exceptionScope,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    ILogger<GetEmployeeOverviewSignalsQueryHandler> logger)
    : IRequestHandler<GetEmployeeOverviewSignalsQuery, Result<EmployeeOverviewSignalsResponse>>
{
    private static readonly string[] WorkModules =
        ["worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints"];

    public async Task<Result<EmployeeOverviewSignalsResponse>> Handle(GetEmployeeOverviewSignalsQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var employeeId = request.EmployeeId;

        var access = await guard.EnsureCanRead(tenantId, employeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeOverviewSignalsResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        var period = EmployeePeriod.Resolve(request.From, request.To, clock.Today);
        if (!period.IsSuccess)
            return Result<EmployeeOverviewSignalsResponse>.Failure(period.Error!, period.StatusCode ?? 400);
        var (from, to) = (period.Value!.From, period.Value.To);

        var attendance = await Safe("attendance", () => sender.Send(new GetEmployeeAttendanceOverviewQuery(employeeId, from, to), ct));
        var discipline = await Safe("discipline", () => sender.Send(new GetEmployeeAttendanceDisciplineQuery(employeeId, from, to), ct));
        var work = await SafeValue("work", async () => await WorkEnabled(tenantId, ct)
            ? await Safe("work", () => sender.Send(new GetEmployeeWorkOverviewQuery(employeeId, from, to), ct))
            : null);
        var approvals = await Safe("approvals", () => sender.Send(new GetEmployeeApprovalActivityQuery(employeeId, from, to), ct));
        var monitoring = await SafeValue("monitoring", () => MonitoringAsync(tenantId, employeeId, from, to, ct));

        var signals = EmployeeSignalCatalogue.Build(new EmployeeSignalInputs(attendance, discipline, work, approvals, monitoring));
        return Result<EmployeeOverviewSignalsResponse>.Success(new EmployeeOverviewSignalsResponse(from, to, signals));
    }

    private async Task<bool> WorkEnabled(Guid tenantId, CancellationToken ct)
    {
        foreach (var key in WorkModules)
            if (await modules.IsModuleEnabledAsync(tenantId, key, ct))
                return true;
        return false;
    }

    private async Task<EmployeeMonitoringSignalCounts?> MonitoringAsync(
        Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (!await toggles.IsEnabledAsync(tenantId, employeeId, MonitoringCapability.ActivityMonitoring, ct))
            return null;

        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var idle = await notifications.CountByTypeAsync(tenantId, employeeId, NotificationType.LongIdleAlert, start, end, ct)
                   + await notifications.CountByTypeAsync(tenantId, employeeId, NotificationType.LowActivityAlert, start, end, ct);

        // Exception cases are visible only to HR, the employee's managers/approvers and exceptions:*
        // holders - not to everyone with employees:read - so the count follows the same scope.
        var scope = await exceptionScope.ResolveAsync(forAction: false, [employeeId], ct);
        int? cases = scope?.CanSee(employeeId) == true
            ? await exceptions.CountDetectedInRangeAsync(tenantId, employeeId, start, end, ct)
            : null;

        return new EmployeeMonitoringSignalCounts(idle, cases);
    }

    private async Task<T?> Safe<T>(string source, Func<Task<Result<T>>> load) where T : class
    {
        try
        {
            var result = await load();
            return result.IsSuccess ? result.Value : null;
        }
        catch (System.Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Overview signal source {Source} failed", source);
            return null;
        }
    }

    private async Task<T?> SafeValue<T>(string source, Func<Task<T?>> load) where T : class
    {
        try
        {
            return await load();
        }
        catch (System.Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Overview signal source {Source} failed", source);
            return null;
        }
    }
}
