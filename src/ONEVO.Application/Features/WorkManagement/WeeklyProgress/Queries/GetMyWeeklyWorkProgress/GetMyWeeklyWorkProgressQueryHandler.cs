using MediatR;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Planner;
using ONEVO.Application.Features.WorkManagement.Planner.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Planner.Services;
using ONEVO.Application.Features.WorkManagement.WeeklyProgress.DTOs;
using ONEVO.Application.Features.WorkManagement.WeeklyProgress.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.WeeklyProgress.Services;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.WeeklyProgress.Queries.GetMyWeeklyWorkProgress;

/// <summary>
/// Weekly Work Progress (2026-10-28 design, sections 5-8). Loads every source once for 14 days (the week before and the
/// requested week, in the employee's zone) and computes both weeks with <see cref="WeeklyWorkProgressCalculator"/>.
/// </summary>
public sealed class GetMyWeeklyWorkProgressQueryHandler(
    ICurrentUser currentUser,
    ICallerIdentityResolver identity,
    IEmployeeRepository employees,
    ILegalEntityRepository legalEntities,
    ICalendarTimezoneResolver timezones,
    IPlannerRepository planner,
    IMeetingRepository meetings,
    IAttendanceReadRepository attendance,
    ICalendarEventRepository calendarEvents,
    ILeaveRequestReadRepository leaveRequests,
    IWeeklyWorkProgressReadRepository weekly,
    IOptions<PlannerOptions> plannerOptions,
    IDateTimeProvider clock)
    : IRequestHandler<GetMyWeeklyWorkProgressQuery, Result<WeeklyWorkProgressResponse>>
{
    public const int MaxWeeksBack = 52;
    private const string NotFoundMessage = "Current employee record was not found.";

    public async Task<Result<WeeklyWorkProgressResponse>> Handle(GetMyWeeklyWorkProgressQuery query, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        if (await identity.ResolveCallerEmployeeIdAsync(tenantId, currentUser.UserId, ct) is not Guid employeeId)
            return Result<WeeklyWorkProgressResponse>.NotFound(NotFoundMessage);
        var employee = await employees.GetByIdAsync(tenantId, employeeId, ct);
        if (employee is null)
            return Result<WeeklyWorkProgressResponse>.NotFound(NotFoundMessage);

        var zoneId = await timezones.ResolveForUserAsync(tenantId, employee.UserId, ct);
        // A zone id this machine does not know falls back to UTC, as the planner does.
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(zoneId, out var zone))
            (zoneId, zone) = ("UTC", TimeZoneInfo.Utc);
        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var currentMonday = MondayOf(today);

        var weekStart = query.WeekStart ?? currentMonday;
        if (weekStart.DayOfWeek != DayOfWeek.Monday || weekStart > currentMonday || weekStart < currentMonday.AddDays(-7 * MaxWeeksBack))
            return Result<WeeklyWorkProgressResponse>.UnprocessableEntity(
                $"weekStart must be a Monday between {currentMonday.AddDays(-7 * MaxWeeksBack):yyyy-MM-dd} and {currentMonday:yyyy-MM-dd}.");

        var lastWeekStart = weekStart.AddDays(-7);
        var weekEnd = weekStart.AddDays(6);
        var (rangeStart, _) = PlannerActuals.DayBounds(lastWeekStart, zone);
        var (thisWeekStartUtc, _) = PlannerActuals.DayBounds(weekStart, zone);
        var (rangeEnd, _) = PlannerActuals.DayBounds(weekStart.AddDays(7), zone);

        var legalEntity = employee.LegalEntityId is Guid entityId
            ? await legalEntities.GetByIdForTenantAsync(tenantId, entityId, ct)
            : null;
        var configured = legalEntity is not null && AttendanceScheduleResolver.IsScheduleConfigured(legalEntity);
        var weekdays = configured ? AttendanceScheduleResolver.ParseWorkingDays(legalEntity!.StandardWorkingDays) : new HashSet<int>();
        var workStart = configured ? legalEntity!.WorkStartTime : null;
        var workEnd = configured ? legalEntity!.WorkEndTime : null;

        var blocks = await planner.ListBlocksAsync(tenantId, employeeId, rangeStart, rangeEnd, ct);
        var sessions = await planner.ListSessionsAsync(tenantId, employeeId, rangeStart, rangeEnd, ct);
        var dayMeetings = await meetings.ListEmployeeDayMeetingsAsync(tenantId, employeeId, rangeStart, rangeEnd, ct);
        var (records, _) = await attendance.ListRecordsAsync(tenantId, [employeeId], lastWeekStart, weekEnd, 0, 31, ct);
        var holidays = (await calendarEvents.ListHolidayDatesAsync(tenantId, lastWeekStart, weekEnd, ct)).ToHashSet();
        var leaves = await leaveRequests.ListApprovedCoveringAsync(tenantId, [employeeId], lastWeekStart, weekEnd, ct);
        var weights = await weekly.ListWeekTaskWeightsAsync(tenantId, employeeId, weekStart, weekEnd, thisWeekStartUtc, rangeEnd, ct);

        var leaveHours = LeaveHoursByDate(leaves.Where(l => l.EmployeeId == employeeId), lastWeekStart, weekEnd, zone, workStart, workEnd);
        WeeklyWorkProgressInput Input(DateOnly start) => new(
            start, zone, now, weekdays, workStart, workEnd, holidays, employee.HireDate, employee.TerminationDate, leaveHours,
            blocks.Select(b => (b.StartAt, b.EndAt)).ToList(),
            dayMeetings.Select(m => new WeeklyMeetingInterval(m.StartAt, m.EndAt, m.IsAllDay, m.Declined, m.Cancelled)).ToList(),
            sessions.Select(s => (s.ClockInAt, s.ClockOutAt)).ToList(),
            records.Where(r => r.EmployeeId == employeeId)
                .Select(r => new WeeklyAttendanceDay(r.Date, r.ActualStart, r.ActualEnd, r.WorkedMinutes, r.BreakMinutes)).ToList());

        return Result<WeeklyWorkProgressResponse>.Success(new WeeklyWorkProgressResponse(
            weekStart, weekEnd, today, zoneId, plannerOptions.Value.Enabled,
            WeeklyWorkProgressCalculator.Compute(Input(weekStart)),
            WeeklyWorkProgressCalculator.Compute(Input(lastWeekStart)),
            TaskWeights(weights)));
    }

    private static DateOnly MondayOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>Per day, approved leave's overlap with that day's work window: null when it covers the whole window (a
    /// full leave day), else its hours. A partial cancellation ends the leave the day before its effective date.</summary>
    private static Dictionary<DateOnly, decimal?> LeaveHoursByDate(IEnumerable<LeaveRequest> leaves, DateOnly from, DateOnly to,
        TimeZoneInfo zone, TimeOnly? workStart, TimeOnly? workEnd)
    {
        var result = new Dictionary<DateOnly, decimal?>();
        if (workStart is not { } ws || workEnd is not { } we || ws >= we)
            return result;
        var windowMinutes = (we - ws).TotalMinutes;
        foreach (var leave in leaves)
        {
            for (var day = from; day <= to; day = day.AddDays(1))
            {
                if (leave.PartialCancelEffectiveDate is { } cancelledFrom && day >= cancelledFrom)
                    break;
                var dayStart = PlannerActuals.DayBounds(day, zone).Start;
                var windowStart = dayStart.Add(ws.ToTimeSpan());
                var windowEnd = dayStart.Add(we.ToTimeSpan());
                var overlapStart = leave.StartAt > windowStart ? leave.StartAt : windowStart;
                var overlapEnd = leave.EndAt < windowEnd ? leave.EndAt : windowEnd;
                if (overlapEnd <= overlapStart)
                    continue;
                // Two leaves on one day add up; a day already fully on leave stays full (null).
                var prior = result.TryGetValue(day, out var hours) ? hours is { } h ? (double)h * 60 : windowMinutes : 0;
                var minutes = prior + (overlapEnd - overlapStart).TotalMinutes;
                result[day] = minutes >= windowMinutes ? null : Math.Round((decimal)minutes / 60m, 2);
            }
        }
        return result;
    }

    private static WeekTaskWeightsDto TaskWeights(IReadOnlyList<WeekTaskWeight> weights)
    {
        decimal Sum(string category) => weights.Where(w => w.Category == category).Sum(w => w.EstimatedHours ?? 0m);
        return new WeekTaskWeightsDto(
            Sum(TaskStatusCategories.Done), Sum(TaskStatusCategories.Active), Sum(TaskStatusCategories.NotStarted),
            weights.Count(w => w.EstimatedHours is null));
    }
}
