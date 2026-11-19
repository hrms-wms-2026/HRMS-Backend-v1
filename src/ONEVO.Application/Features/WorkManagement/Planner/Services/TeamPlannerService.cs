using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Planner.Contracts;
using ONEVO.Application.Features.WorkManagement.Planner.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Planner.Services;

/// <summary>
/// Team planner, Phase A (2026-11-03 design): read-only days of a person, for themself or their reporting-line ancestors,
/// with private meetings masked and leave shown by status only. Anyone else gets 404, as for the day planner.
/// </summary>
public sealed class TeamPlannerService
{
    public const string LeaveApproved = "approved";
    public const string LeavePending = "pending";
    public const string LeaveBusy = "busy";

    private readonly IPlannerRepository _planner;
    private readonly IMeetingRepository _meetings;
    private readonly ICalendarTimezoneResolver _timezones;
    private readonly IEmployeeRepository _employees;
    private readonly ILeaveRequestReadRepository _leave;
    private readonly IDateTimeProvider _clock;
    private readonly IOptions<PlannerOptions> _options;

    public TeamPlannerService(IPlannerRepository planner, IMeetingRepository meetings, ICalendarTimezoneResolver timezones,
        IEmployeeRepository employees, ILeaveRequestReadRepository leave, IDateTimeProvider clock, IOptions<PlannerOptions> options)
    {
        _planner = planner;
        _meetings = meetings;
        _timezones = timezones;
        _employees = employees;
        _leave = leave;
        _clock = clock;
        _options = options;
    }

    /// <param name="callerCanReadLeave">The caller holds leave:read; without it others' approved leave reads as busy time.</param>
    public async Task<Result<PlannerPersonRangeResponse>> GetPersonRangeAsync(Guid tenantId, Guid callerEmployeeId, Guid employeeId,
        DateOnly from, DateOnly to, bool callerCanReadLeave, CancellationToken ct)
    {
        if (!_options.Value.Enabled)
            return PlannerService.Disabled<PlannerPersonRangeResponse>();
        if (!ValidRange(from, to))
            return PlannerService.InvalidRange<PlannerPersonRangeResponse>();
        var own = callerEmployeeId == employeeId;
        if (!own && !(await _employees.ListAncestorChainEmployeeIdsAsync(tenantId, employeeId, ct)).Contains(callerEmployeeId))
            return PlannerService.NotFound<PlannerPersonRangeResponse>();
        var employee = (await _employees.ListByIdsAsync(tenantId, [employeeId], ct)).GetValueOrDefault(employeeId);
        if (employee is null)
            return PlannerService.NotFound<PlannerPersonRangeResponse>();
        var zone = await PlannerZones.ResolveForAsync(_timezones, tenantId, employee, ct);
        var canReadLeave = own || callerCanReadLeave;
        var leave = await LoadLeaveAsync(tenantId, [employeeId], from, to, canReadLeave, ct);

        var days = await BuildDaysAsync(tenantId, employeeId, zone, from, to, detail: true, maskPrivate: !own, canReadLeave, leave, ct);
        return Result<PlannerPersonRangeResponse>.Success(new PlannerPersonRangeResponse(employeeId, NameOf(employee),
            zone.Id, PlannerZones.TodayIn(_clock.UtcNow, zone.Info), days));
    }

    /// <summary>The team's <paramref name="date"/> (default: today in the caller's zone), with every block, meeting and leave.</summary>
    public async Task<Result<TeamPlannerResponse>> GetTeamDayAsync(Guid tenantId, Guid callerEmployeeId, Guid? managerId,
        DateOnly? date, bool callerCanReadLeave, CancellationToken ct)
    {
        if (!_options.Value.Enabled)
            return PlannerService.Disabled<TeamPlannerResponse>();
        var day = date;
        if (day is null)
        {
            var zone = await PlannerZones.ResolveAsync(_employees, _timezones, tenantId, callerEmployeeId, ct);
            day = PlannerZones.TodayIn(_clock.UtcNow, zone?.Info ?? TimeZoneInfo.Utc);
        }
        return await GetTeamAsync(tenantId, callerEmployeeId, managerId, day.Value, day.Value, detail: true, callerCanReadLeave, ct);
    }

    /// <summary>
    /// The direct reports of <paramref name="managerId"/> (default: the caller), each with their days from
    /// <paramref name="from"/> to <paramref name="to"/> in their own zone. The caller must be that manager or one of their
    /// reporting-line ancestors; anyone else gets 404. Without <paramref name="detail"/> only the minutes come back.
    /// </summary>
    public async Task<Result<TeamPlannerResponse>> GetTeamAsync(Guid tenantId, Guid callerEmployeeId, Guid? managerId,
        DateOnly from, DateOnly to, bool detail, bool callerCanReadLeave, CancellationToken ct)
    {
        if (!_options.Value.Enabled)
            return PlannerService.Disabled<TeamPlannerResponse>();
        if (!ValidRange(from, to))
            return PlannerService.InvalidRange<TeamPlannerResponse>();
        var managerEmployeeId = managerId ?? callerEmployeeId;
        if (managerEmployeeId != callerEmployeeId
            && !(await _employees.ListAncestorChainEmployeeIdsAsync(tenantId, managerEmployeeId, ct)).Contains(callerEmployeeId))
            return PlannerService.NotFound<TeamPlannerResponse>();
        var manager = (await _employees.ListByIdsAsync(tenantId, [managerEmployeeId], ct)).GetValueOrDefault(managerEmployeeId);
        if (manager is null)
            return PlannerService.NotFound<TeamPlannerResponse>();

        var reportIds = (await _employees.ListDirectReportEmployeeIdsAsync(tenantId, [managerEmployeeId], ct)).Distinct().ToList();
        var reports = await _employees.ListByIdsAsync(tenantId, reportIds, ct);
        // Leave for the whole team in three reads, not three per member.
        var leave = await LoadLeaveAsync(tenantId, reports.Keys.ToList(), from, to, callerCanReadLeave, ct);
        var members = new List<TeamPlannerMember>(reports.Count);
        foreach (var report in reports.Values.OrderBy(e => e.FirstName).ThenBy(e => e.LastName))
        {
            var zone = await PlannerZones.ResolveForAsync(_timezones, tenantId, report, ct);
            var directReports = (await _employees.ListDirectReportEmployeeIdsAsync(tenantId, [report.Id], ct)).Distinct().Count();
            var days = await BuildDaysAsync(tenantId, report.Id, zone, from, to, detail, maskPrivate: true, callerCanReadLeave, leave, ct);
            members.Add(new TeamPlannerMember(report.Id, NameOf(report), directReports, zone.Id, days));
        }
        return Result<TeamPlannerResponse>.Success(new TeamPlannerResponse(managerEmployeeId, NameOf(manager), from, to, members));
    }

    internal static bool ValidRange(DateOnly from, DateOnly to)
        => to >= from && to.DayNumber - from.DayNumber + 1 <= PlannerService.MaxRangeDays;

    internal static string NameOf(Employee employee) => $"{employee.FirstName} {employee.LastName}".Trim();

    /// <summary>The person's days from <paramref name="from"/> to <paramref name="to"/> in their zone. Without
    /// <paramref name="detail"/> the lists are empty but every minute figure and OnLeave stay.</summary>
    /// <summary>Approved, partially cancelled and (when the caller may read leave) pending leave for <paramref name="employeeIds"/>.
    /// Leave reads by UTC date; a day either side covers every zone, then each day keeps what overlaps it.</summary>
    private async Task<LeaveRows> LoadLeaveAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds, DateOnly from, DateOnly to,
        bool canReadLeave, CancellationToken ct)
    {
        var (first, last) = (from.AddDays(-1), to.AddDays(1));
        var approved = await _leave.ListApprovedCoveringAsync(tenantId, employeeIds, first, last, ct);
        var partial = await _leave.ListPartiallyCancelledCoveringAsync(tenantId, employeeIds, first, last, ct);
        var pending = canReadLeave ? await _leave.ListPendingCoveringAsync(tenantId, employeeIds, first, last, ct) : [];
        return new LeaveRows(approved.ToLookup(l => l.EmployeeId), partial.ToLookup(l => l.EmployeeId), pending.ToLookup(l => l.EmployeeId));
    }

    private sealed record LeaveRows(ILookup<Guid, LeaveRequest> Approved, ILookup<Guid, LeaveRequest> PartiallyCancelled,
        ILookup<Guid, LeaveRequest> Pending);

    private async Task<IReadOnlyList<PlannerPersonDay>> BuildDaysAsync(Guid tenantId, Guid employeeId, PlannerZones.Zone zone,
        DateOnly from, DateOnly to, bool detail, bool maskPrivate, bool canReadLeave, LeaveRows leave, CancellationToken ct)
    {
        var (start, _) = PlannerActuals.DayBounds(from, zone.Info);
        var (_, end) = PlannerActuals.DayBounds(to, zone.Info);
        var now = _clock.UtcNow;
        var blocks = await _planner.ListBlocksAsync(tenantId, employeeId, start, end, ct);
        var tasks = await _planner.GetTasksAsync(tenantId, employeeId, blocks.Select(b => b.TaskId).Distinct().ToList(), ct);
        var sessions = await _planner.ListSessionsAsync(tenantId, employeeId, start, end, ct);
        var meetings = await _meetings.ListEmployeeDayMeetingsAsync(tenantId, employeeId, start, end, ct);
        var legalEntity = await _planner.GetEmployeeLegalEntityAsync(tenantId, employeeId, ct);
        // A partial cancel keeps the days before its effective date (in the employee's zone) as leave.
        var approved = leave.Approved[employeeId].Select(l => (Start: l.StartAt, End: l.EndAt))
            .Concat(leave.PartiallyCancelled[employeeId].Select(l => (Start: l.StartAt,
                End: Min(l.EndAt, PlannerZones.At(l.PartialCancelEffectiveDate!.Value, TimeOnly.MinValue, zone.Info)))))
            .Where(l => l.End > l.Start)
            .ToList();
        var pending = leave.Pending[employeeId].Select(l => (Start: l.StartAt, End: l.EndAt)).ToList();
        var blocksByDay = blocks.ToLookup(b => PlannerZones.DayOf(b.StartAt, zone.Info));

        var days = new List<PlannerPersonDay>(to.DayNumber - from.DayNumber + 1);
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var (dayStart, dayEnd) = PlannerActuals.DayBounds(day, zone.Info);
            var window = PlannerWorkWindows.For(legalEntity, day);
            DateTimeOffset? workStart = window is { IsWorkingDay: true } ? PlannerZones.At(day, window.WorkStart, zone.Info) : null;
            DateTimeOffset? workEnd = window is { IsWorkingDay: true } ? PlannerZones.At(day, window.WorkEnd, zone.Info) : null;

            var blockViews = blocksByDay[day].Select(b =>
            {
                var task = tasks.GetValueOrDefault(b.TaskId);
                var actual = PlannerActuals.TaskMinutes(sessions.Where(s => s.TaskId == b.TaskId).Select(s => (s.ClockInAt, s.ClockOutAt)),
                    dayStart, dayEnd, now);
                return new PlannerRangeBlockView(b.Id, b.TaskId, task?.ProjectId ?? Guid.Empty, task?.ProjectName ?? string.Empty,
                    task?.Title ?? string.Empty, b.StartAt, b.EndAt, (int)(b.EndAt - b.StartAt).TotalMinutes, actual,
                    task?.AssignedToEmployee ?? false, task?.CategoryName ?? string.Empty, task?.CategoryKind ?? TaskCategoryKinds.Task, task?.CategoryIcon);
            }).ToList();

            var meetingViews = meetings.Where(m => m.StartAt < dayEnd && m.EndAt > dayStart).Select(m =>
            {
                var minutes = PlannerActuals.MeetingMinutesInDay(m.StartAt, m.EndAt, m.IsAllDay, dayStart, dayEnd);
                var counts = !m.Cancelled && !m.Declined;
                return new PlannerMeetingView(m.TaskId, m.SeriesId, m.OriginalStart, m.StartAt, m.EndAt,
                    maskPrivate && m.IsPrivate ? PlannerService.PrivateTitle : m.Title, m.IsPrivate, m.Cancelled, m.Declined,
                    counts ? minutes : 0, PlannerActuals.MeetingMinutes(minutes, m.Declined, m.Cancelled, ended: now >= m.EndAt));
            }).ToList();

            var approvedToday = approved.Where(l => l.Start < dayEnd && l.End > dayStart).ToList();
            var leaveViews = approvedToday
                .Select(l => new PlannerLeaveView(Max(l.Start, dayStart), Min(l.End, dayEnd), canReadLeave ? LeaveApproved : LeaveBusy))
                .Concat(pending.Where(l => l.Start < dayEnd && l.End > dayStart)
                    .Select(l => new PlannerLeaveView(Max(l.Start, dayStart), Min(l.End, dayEnd), LeavePending)))
                .ToList();

            var working = window?.WorkingMinutes ?? 0;
            var leaveInWindow = workStart is { } ws && workEnd is { } we
                ? approvedToday.Sum(l => Math.Max(0, (int)Math.Round((Min(l.End, we) - Max(l.Start, ws)).TotalMinutes)))
                : 0;
            var capacity = Math.Max(0, working - leaveInWindow);
            var onLeave = canReadLeave && working > 0 && leaveInWindow > 0 && capacity == 0;

            days.Add(new PlannerPersonDay(day, window, workStart, workEnd,
                detail ? blockViews : [], detail ? meetingViews : [], detail ? leaveViews : [],
                blockViews.Sum(v => v.PlannedMinutes), meetingViews.Sum(v => v.PlannedMinutes), working, capacity, onLeave));
        }
        return days;
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
