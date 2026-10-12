using System.Globalization;
using MediatR;
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
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Notifications.Entities;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeSignalItems;

/// <summary>
/// The concrete items behind one Overview signal (see <see cref="EmployeeSignalCatalogue"/>).
/// Every branch reuses the exact rule that produces that signal's count, so Total always equals
/// the signal's Value. A source the signals endpoint would omit (module off, toggle off, outside
/// exception scope) is a 403 here.
/// </summary>
public sealed class GetEmployeeSignalItemsQueryHandler(
    IEmployeeReadAccessGuard guard,
    IEmployeeAttendancePeriodReader reader,
    IWorkTaskRepository tasks,
    ISender sender,
    IModuleEntitlementService modules,
    IMonitoringToggleResolver toggles,
    INotificationRepository notifications,
    IExceptionRepository exceptions,
    IExceptionScopeResolver exceptionScope,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeSignalItemsQuery, Result<EmployeeSignalItemsResponse>>
{
    public const int MaxItems = 50;

    private static readonly HashSet<string> AttendanceKeys =
        ["absent_days", "missing_clock_outs", "late_clock_ins", "early_clock_outs", "short_hours_days", "off_schedule_work", "over_break"];

    public async Task<Result<EmployeeSignalItemsResponse>> Handle(GetEmployeeSignalItemsQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeSignalItemsResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        var period = EmployeePeriod.Resolve(request.From, request.To, clock.Today);
        if (!period.IsSuccess)
            return Result<EmployeeSignalItemsResponse>.Failure(period.Error!, period.StatusCode ?? 400);

        if (AttendanceKeys.Contains(request.Key))
        {
            var data = await reader.LoadAsync(tenantId, request.EmployeeId, access.Value!.LegalEntityId, period.Value!, ct);
            return Result<EmployeeSignalItemsResponse>.Success(AttendanceItems(request.Key, data));
        }

        var legalEntityId = access.Value!.LegalEntityId;
        var (from, to) = (period.Value!.From, period.Value.To);
        var utcStart = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var utcEnd = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        switch (request.Key)
        {
            case "location_violations":
            {
                if (!await toggles.IsEnabledForEmployeeAsync(tenantId, request.EmployeeId, MonitoringCapability.WorkLocationVerification, ct))
                    return Hidden();
                var data = await reader.LoadAsync(tenantId, request.EmployeeId, legalEntityId, period.Value, ct);
                return await AlertsAsync(tenantId, request, data.Timezone, [NotificationType.OutsideWorkLocationAlert], data.RangeStartUtc, data.RangeEndUtc, ct);
            }
            case "idle_activity_alerts":
            {
                if (!await toggles.IsEnabledForEmployeeAsync(tenantId, request.EmployeeId, MonitoringCapability.ActivityMonitoring, ct))
                    return Hidden();
                var data = await reader.LoadAsync(tenantId, request.EmployeeId, legalEntityId, period.Value, ct);
                return await AlertsAsync(tenantId, request, data.Timezone, [NotificationType.LongIdleAlert, NotificationType.LowActivityAlert], utcStart, utcEnd, ct);
            }
            case "monitoring_exceptions":
            {
                if (!await toggles.IsEnabledForEmployeeAsync(tenantId, request.EmployeeId, MonitoringCapability.ActivityMonitoring, ct))
                    return Hidden();
                var scope = await exceptionScope.ResolveAsync(forAction: false, [request.EmployeeId], ct);
                if (scope?.CanSee(request.EmployeeId) != true)
                    return Hidden();
                var data = await reader.LoadAsync(tenantId, request.EmployeeId, legalEntityId, period.Value, ct);
                var total = await exceptions.CountDetectedInRangeAsync(tenantId, request.EmployeeId, utcStart, utcEnd, ct);
                var cases = await exceptions.ListDetectedInRangeAsync(tenantId, request.EmployeeId, utcStart, utcEnd, MaxItems, ct);
                var items = cases.Select(c => new EmployeeSignalItem(
                    "exception_case", c.Id.ToString(), LocalDate(c.DetectedAt, data.Timezone), c.Title, c.Description,
                    OccurredAt: c.DetectedAt)).ToList();
                return Result<EmployeeSignalItemsResponse>.Success(new(request.Key, total, data.Timezone.Id, items));
            }
            case "overdue_tasks":
            {
                if (!await EmployeeOverviewWorkModules.IsEnabledAsync(modules, tenantId, ct))
                    return Hidden();
                var rows = await tasks.ListForEmployeePeriodAsync(tenantId, request.EmployeeId, from, to, ct);
                var asOf = to < clock.Today ? to : clock.Today;
                var items = rows
                    .Where(r => EmployeeTaskPeriodCalculator.IsOverdue(r, asOf))
                    .Select(r => new EmployeeSignalItem(
                        "task", r.TaskId.ToString(), r.DueDate!.Value, r.Title, r.ProjectName,
                        ProjectId: r.ProjectId, ProjectName: r.ProjectName, DueDate: r.DueDate,
                        CarriedOver: r.IsCarriedOver, DaysOverdue: EmployeeTaskPeriodCalculator.DaysOverdue(r, asOf)))
                    .ToList();
                return Result<EmployeeSignalItemsResponse>.Success(Page(request.Key, null, items));
            }
            case "pending_approvals":
            {
                var activity = await sender.Send(new GetEmployeeApprovalActivityQuery(request.EmployeeId, from, to, AllItems: true), ct);
                if (!activity.IsSuccess)
                    return Result<EmployeeSignalItemsResponse>.Failure(activity.Error!, activity.StatusCode ?? 400);
                var items = activity.Value!.Items
                    .Where(i => i.Status == "pending")
                    .Select(i => new EmployeeSignalItem(
                        "approval", i.Id, DateOnly.FromDateTime(i.RequestedAt.UtcDateTime), i.Label, i.Detail,
                        ApprovalKind: i.Kind, RequestedAt: i.RequestedAt, ApproverName: i.ApproverName))
                    .ToList();
                return Result<EmployeeSignalItemsResponse>.Success(Page(request.Key, null, items));
            }
        }

        return NotFound(request.Key);
    }

    private async Task<Result<EmployeeSignalItemsResponse>> AlertsAsync(
        Guid tenantId, GetEmployeeSignalItemsQuery request, TimeZoneInfo timezone, NotificationType[] types,
        DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, CancellationToken ct)
    {
        var total = 0;
        foreach (var type in types)
            total += await notifications.CountByTypeAsync(tenantId, request.EmployeeId, type, fromUtc, toUtcExclusive, ct);
        var rows = await notifications.ListByTypesAsync(tenantId, request.EmployeeId, types, fromUtc, toUtcExclusive, MaxItems, ct);
        var items = rows.Select(n => new EmployeeSignalItem(
            "monitoring_alert", n.Id.ToString(), LocalDate(n.CreatedAt, timezone), n.Title, n.Message,
            OccurredAt: n.CreatedAt)).ToList();
        return Result<EmployeeSignalItemsResponse>.Success(new(request.Key, total, timezone.Id, items));
    }

    private static DateOnly LocalDate(DateTimeOffset at, TimeZoneInfo timezone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, timezone).DateTime);

    private static EmployeeSignalItemsResponse AttendanceItems(string key, AttendancePeriodData data)
    {
        if (key == "over_break")
        {
            var over = AttendancePeriodCalculator.OverBreakDays(data);
            return Page(key, data.Timezone.Id, over.Select(o => Day(o.Date, $"{o.MinutesOver} min over allowance")).ToList());
        }

        var dates = AttendancePeriodCalculator.Classify(data).Dates;
        var items = key switch
        {
            "absent_days" => dates.Absent.Select(d => Day(d, "No clock-in or approved leave")),
            "missing_clock_outs" => dates.MissingClockOuts.Select(d => Day(d, "No clock-out recorded")),
            "late_clock_ins" => dates.Late.Select(d => Day(d, "Clocked in after the scheduled start")),
            "early_clock_outs" => dates.EarlyDepartures.Select(d => Day(d, "Left before the scheduled end")),
            "short_hours_days" => dates.ShortHours.Select(d => Day(d, "Worked less than the required hours")),
            _ => dates.WorkedOnNonWorkingDay.Select(d => Day(d, "Worked on a non-working day"))
                .Concat(dates.WorkedDuringTimeOff.Select(d => Day(d, "Worked during approved time off")))
        };
        return Page(key, data.Timezone.Id, items.ToList());
    }

    private static EmployeeSignalItem Day(DateOnly date, string subtitle) =>
        new("attendance_day", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), date,
            date.ToString("ddd d MMM", CultureInfo.InvariantCulture), subtitle);

    /// <summary>Newest first, capped at MaxItems; Total is the uncapped count.</summary>
    private static EmployeeSignalItemsResponse Page(string key, string? timezone, IReadOnlyList<EmployeeSignalItem> all) =>
        new(key, all.Count, timezone,
            all.OrderByDescending(i => i.Date).ThenByDescending(i => i.OccurredAt).Take(MaxItems).ToList());

    private static Result<EmployeeSignalItemsResponse> NotFound(string key) =>
        Result<EmployeeSignalItemsResponse>.Failure($"Unknown overview signal '{key}'.", 404);

    private static Result<EmployeeSignalItemsResponse> Hidden() =>
        Result<EmployeeSignalItemsResponse>.Failure("This signal's source is not available for this employee.", 403);
}
