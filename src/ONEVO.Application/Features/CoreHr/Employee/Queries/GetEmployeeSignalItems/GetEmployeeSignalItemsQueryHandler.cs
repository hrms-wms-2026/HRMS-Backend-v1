using System.Globalization;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

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

        return NotFound(request.Key);
    }

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
