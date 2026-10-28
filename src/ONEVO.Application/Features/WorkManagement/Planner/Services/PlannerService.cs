using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Application.Features.WorkManagement.Planner.Contracts;
using ONEVO.Application.Features.WorkManagement.Planner.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Planner.Entities;

namespace ONEVO.Application.Features.WorkManagement.Planner.Services;

/// <summary>
/// Day planner (2026-10-26 design, Part B; 2026-10-27 Planner = Calendar design). An employee plans today and up to
/// <see cref="PlanningHorizonDays"/> days ahead, in their own timezone, as task blocks next to their meetings; past days
/// are read-only and a block never touches the task. Their reporting-line managers (any ancestor) read any day up to the
/// horizon, with private meetings masked; anyone else gets 404.
/// </summary>
public sealed class PlannerService
{
    public const int MinimumBlockMinutes = 15;
    /// <summary>How many days after today can still be planned (spec section 5.1).</summary>
    public const int PlanningHorizonDays = 28;
    /// <summary>The longest range one read returns: a month grid (spec section 5.2).</summary>
    public const int MaxRangeDays = 42;
    private const string PrivateMeetingTitle = "Private meeting";
    private const string NotFoundMessage = "Planner day not found.";

    private readonly IPlannerRepository _planner;
    private readonly IMeetingRepository _meetings;
    private readonly ICalendarTimezoneResolver _timezones;
    private readonly IEmployeeRepository _employees;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;
    private readonly IOptions<PlannerOptions> _options;

    public PlannerService(IPlannerRepository planner, IMeetingRepository meetings, ICalendarTimezoneResolver timezones,
        IEmployeeRepository employees, IUnitOfWork unitOfWork, IDateTimeProvider clock, IOptions<PlannerOptions> options)
    {
        _planner = planner;
        _meetings = meetings;
        _timezones = timezones;
        _employees = employees;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _options = options;
    }

    public async Task<Result<PlannerDayResponse>> GetDayAsync(Guid tenantId, Guid callerEmployeeId, Guid employeeId, DateOnly? date,
        CancellationToken ct)
    {
        if (!_options.Value.Enabled)
            return Disabled<PlannerDayResponse>();
        var own = callerEmployeeId == employeeId;
        if (!own && !(await _employees.ListAncestorChainEmployeeIdsAsync(tenantId, employeeId, ct)).Contains(callerEmployeeId))
            return NotFound<PlannerDayResponse>();

        var zone = await ZoneOfAsync(tenantId, employeeId, ct);
        if (zone is null)
            return NotFound<PlannerDayResponse>();
        var today = TodayIn(zone.Value.Info);
        var day = date ?? today;
        if (day > today.AddDays(PlanningHorizonDays))
            return BeyondHorizon<PlannerDayResponse>();

        return Result<PlannerDayResponse>.Success(
            await ComposeAsync(tenantId, employeeId, day, zone.Value, editable: own && DayCheck(day, today) is null, maskPrivate: !own, ct));
    }

    /// <summary>The caller's own days from <paramref name="from"/> to <paramref name="to"/> for the calendar grid: blocks, the
    /// work window and whether each day can still change. One read per concern for the whole range.</summary>
    public async Task<Result<PlannerRangeResponse>> GetRangeAsync(Guid tenantId, Guid callerEmployeeId, DateOnly from, DateOnly to,
        CancellationToken ct)
    {
        if (!_options.Value.Enabled)
            return Disabled<PlannerRangeResponse>();
        if (to < from || to.DayNumber - from.DayNumber + 1 > MaxRangeDays)
            return Result<PlannerRangeResponse>.Failure($"A range runs forwards and covers at most {MaxRangeDays} days.", 400,
                PlannerErrorCodes.InvalidRange);
        var zone = await ZoneOfAsync(tenantId, callerEmployeeId, ct);
        if (zone is null)
            return NotFound<PlannerRangeResponse>();

        var today = TodayIn(zone.Value.Info);
        var (start, _) = Bounds(from, zone.Value.Info);
        var (_, end) = Bounds(to, zone.Value.Info);
        var now = _clock.UtcNow;
        var blocks = await _planner.ListBlocksAsync(tenantId, callerEmployeeId, start, end, ct);
        var tasks = await _planner.GetTasksAsync(tenantId, callerEmployeeId, blocks.Select(b => b.TaskId).Distinct().ToList(), ct);
        var sessions = await _planner.ListSessionsAsync(tenantId, callerEmployeeId, start, end, ct);
        var legalEntity = await _planner.GetEmployeeLegalEntityAsync(tenantId, callerEmployeeId, ct);
        var blocksByDay = blocks.ToLookup(b => DayOf(b.StartAt, zone.Value.Info));

        var days = new List<PlannerRangeDay>(to.DayNumber - from.DayNumber + 1);
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var (dayStart, dayEnd) = Bounds(day, zone.Value.Info);
            var views = blocksByDay[day].Select(b =>
            {
                var task = tasks.GetValueOrDefault(b.TaskId);
                var actual = PlannerActuals.TaskMinutes(sessions.Where(s => s.TaskId == b.TaskId).Select(s => (s.ClockInAt, s.ClockOutAt)),
                    dayStart, dayEnd, now);
                return new PlannerRangeBlockView(b.Id, b.TaskId, task?.ProjectId ?? Guid.Empty, task?.ProjectName ?? string.Empty, task?.Title ?? string.Empty,
                    b.StartAt, b.EndAt, (int)(b.EndAt - b.StartAt).TotalMinutes, actual, task?.AssignedToEmployee ?? false,
                    task?.CategoryName ?? string.Empty, task?.CategoryKind ?? TaskCategoryKinds.Task);
            }).ToList();
            var window = PlannerWorkWindows.For(legalEntity, day);
            days.Add(new PlannerRangeDay(day, DayCheck(day, today) is null, window, views, views.Sum(v => v.PlannedMinutes),
                window?.WorkingMinutes ?? 0));
        }
        return Result<PlannerRangeResponse>.Success(new PlannerRangeResponse(zone.Value.Id, today, days));
    }

    /// <summary>The caller's Focus list: open assigned tasks grouped by due date, with what is already planned from today on.</summary>
    public async Task<Result<PlannerFocusResponse>> GetFocusAsync(Guid tenantId, Guid callerEmployeeId, CancellationToken ct)
    {
        if (!_options.Value.Enabled)
            return Disabled<PlannerFocusResponse>();
        var zone = await ZoneOfAsync(tenantId, callerEmployeeId, ct);
        if (zone is null)
            return NotFound<PlannerFocusResponse>();

        var today = TodayIn(zone.Value.Info);
        var (start, _) = Bounds(today, zone.Value.Info);
        var (_, end) = Bounds(today.AddDays(PlanningHorizonDays), zone.Value.Info);
        var rows = await _planner.ListFocusTasksAsync(tenantId, callerEmployeeId, _clock.UtcNow, ct);
        var blocks = await _planner.ListBlocksAsync(tenantId, callerEmployeeId, start, end, ct);
        return Result<PlannerFocusResponse>.Success(PlannerFocus.Build(rows, blocks, today, zone.Value.Info));
    }

    public Task<Result<PlannerDayResponse>> CreateBlockAsync(Guid tenantId, Guid callerEmployeeId, CreatePlannedBlockRequest request,
        CancellationToken ct)
        => _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            // Under the plan lock, so a second device's write checks for overlap only after this one is in.
            await _planner.LockPlanAsync(tenantId, callerEmployeeId, innerCt);
            var check = await CheckWriteAsync(tenantId, callerEmployeeId, request.StartAt, request.EndAt, exceptBlockId: null, innerCt);
            if (check.Failure is { } failure)
                return failure;
            var task = (await _planner.GetTasksAsync(tenantId, callerEmployeeId, [request.TaskId], innerCt)).GetValueOrDefault(request.TaskId);
            if (task is null || task.IsMeeting || !task.AssignedToEmployee)
                return Result<PlannerDayResponse>.Failure("Only a task assigned to you can be planned.", 400, PlannerErrorCodes.TaskNotPlannable);

            _planner.AddBlock(new PlannedTaskBlock
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = callerEmployeeId, TaskId = request.TaskId,
                StartAt = request.StartAt.ToUniversalTime(), EndAt = request.EndAt.ToUniversalTime(), CreatedById = callerEmployeeId,
            });
            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result<PlannerDayResponse>.Success(await ComposeDayOfAsync(tenantId, callerEmployeeId, request.StartAt, check.Zone, innerCt));
        }, ct);

    public async Task<Result<PlannerDayResponse>> MoveBlockAsync(Guid tenantId, Guid callerEmployeeId, Guid blockId,
        MovePlannedBlockRequest request, CancellationToken ct)
    {
        if (!_options.Value.Enabled)
            return Disabled<PlannerDayResponse>();
        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            await _planner.LockPlanAsync(tenantId, callerEmployeeId, innerCt);
            var block = await _planner.GetBlockTrackedAsync(tenantId, callerEmployeeId, blockId, innerCt);
            if (block is null)
                return NotFound<PlannerDayResponse>();
            var check = await CheckWriteAsync(tenantId, callerEmployeeId, request.StartAt, request.EndAt, blockId, innerCt);
            if (check.Failure is { } failure && check.Zone == default)
                return failure;
            // A block already on a past day stays where it is, wherever the request would take it.
            if (DayCheck(DayOf(block.StartAt, check.Zone.Info), TodayIn(check.Zone.Info)) is { } locked)
                return locked;
            if (check.Failure is { } writeFailure)
                return writeFailure;

            block.StartAt = request.StartAt.ToUniversalTime();
            block.EndAt = request.EndAt.ToUniversalTime();
            block.UpdatedAt = _clock.UtcNow;
            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result<PlannerDayResponse>.Success(await ComposeDayOfAsync(tenantId, callerEmployeeId, request.StartAt, check.Zone, innerCt));
        }, ct);
    }

    public async Task<Result<PlannerDayResponse>> DeleteBlockAsync(Guid tenantId, Guid callerEmployeeId, Guid blockId, CancellationToken ct)
    {
        if (!_options.Value.Enabled)
            return Disabled<PlannerDayResponse>();
        var block = await _planner.GetBlockTrackedAsync(tenantId, callerEmployeeId, blockId, ct);
        if (block is null)
            return NotFound<PlannerDayResponse>();
        var zone = await ZoneOfAsync(tenantId, callerEmployeeId, ct);
        if (zone is null)
            return NotFound<PlannerDayResponse>();
        if (DayCheck(DayOf(block.StartAt, zone.Value.Info), TodayIn(zone.Value.Info)) is { } locked)
            return locked;

        _planner.RemoveBlock(block);
        await _unitOfWork.SaveChangesAsync(ct);
        return Result<PlannerDayResponse>.Success(await ComposeDayOfAsync(tenantId, callerEmployeeId, block.StartAt, zone.Value, ct));
    }

    private readonly record struct Zone(string Id, TimeZoneInfo Info);

    private readonly record struct WriteCheck(Zone Zone, Result<PlannerDayResponse>? Failure);

    /// <summary>Flag on, a valid block inside one editable day (today up to the horizon), no overlap with their other blocks.</summary>
    private async Task<WriteCheck> CheckWriteAsync(Guid tenantId, Guid employeeId, DateTimeOffset start, DateTimeOffset end,
        Guid? exceptBlockId, CancellationToken ct)
    {
        if (!_options.Value.Enabled)
            return new(default, Disabled<PlannerDayResponse>());
        var zone = await ZoneOfAsync(tenantId, employeeId, ct);
        if (zone is null)
            return new(default, NotFound<PlannerDayResponse>());
        if ((end - start).TotalMinutes < MinimumBlockMinutes)
            return new(zone.Value, Result<PlannerDayResponse>.Failure(
                $"A block ends after it starts and lasts at least {MinimumBlockMinutes} minutes.", 400, PlannerErrorCodes.InvalidTime));
        var day = DayOf(start, zone.Value.Info);
        if (DayOf(end.AddTicks(-1), zone.Value.Info) != day)
            return new(zone.Value, Result<PlannerDayResponse>.Failure("A block starts and ends on the same day.", 400,
                PlannerErrorCodes.InvalidTime));
        if (DayCheck(day, TodayIn(zone.Value.Info)) is { } dayFailure)
            return new(zone.Value, dayFailure);
        if (await _planner.OverlapsAsync(tenantId, employeeId, start, end, exceptBlockId, ct))
            return new(zone.Value, Result<PlannerDayResponse>.Failure("This overlaps another planned block.", 409, PlannerErrorCodes.BlockOverlap));
        return new(zone.Value, null);
    }

    /// <summary>The caller's own day that <paramref name="instant"/> falls on, after a write to it (so it is editable).</summary>
    private Task<PlannerDayResponse> ComposeDayOfAsync(Guid tenantId, Guid employeeId, DateTimeOffset instant, Zone zone,
        CancellationToken ct)
        => ComposeAsync(tenantId, employeeId, DayOf(instant, zone.Info), zone, editable: true, maskPrivate: false, ct);

    /// <summary>Null when <paramref name="day"/> can be planned: today up to <see cref="PlanningHorizonDays"/> ahead.</summary>
    private static Result<PlannerDayResponse>? DayCheck(DateOnly day, DateOnly today)
    {
        if (day < today)
            return Result<PlannerDayResponse>.Failure("Past days can't be changed.", 400, PlannerErrorCodes.DayReadOnly);
        if (day > today.AddDays(PlanningHorizonDays))
            return BeyondHorizon<PlannerDayResponse>();
        return null;
    }

    private async Task<PlannerDayResponse> ComposeAsync(Guid tenantId, Guid employeeId, DateOnly day, Zone zone, bool editable,
        bool maskPrivate, CancellationToken ct)
    {
        var (start, end) = Bounds(day, zone.Info);
        var now = _clock.UtcNow;
        var blocks = await _planner.ListBlocksAsync(tenantId, employeeId, start, end, ct);
        var meetings = await _meetings.ListEmployeeDayMeetingsAsync(tenantId, employeeId, start, end, ct);
        var sessions = await _planner.ListSessionsAsync(tenantId, employeeId, start, end, ct);
        var tasks = await _planner.GetTasksAsync(tenantId, employeeId, blocks.Select(b => b.TaskId).Distinct().ToList(), ct);
        var due = await _planner.ListDueAssignedTasksAsync(tenantId, employeeId, day, ct);
        var attendance = await _planner.GetAttendanceAsync(tenantId, employeeId, day, ct);

        var actualByTask = sessions.GroupBy(s => s.TaskId).ToDictionary(g => g.Key,
            g => PlannerActuals.TaskMinutes(g.Select(s => (s.ClockInAt, s.ClockOutAt)), start, end, now));
        var meetingViews = meetings.Select(m =>
        {
            var minutes = MinutesInDay(m.StartAt, m.EndAt, m.IsAllDay, start, end);
            var counts = !m.Cancelled && !m.Declined;
            return new PlannerMeetingView(m.TaskId, m.SeriesId, m.OriginalStart, m.StartAt, m.EndAt,
                maskPrivate && m.IsPrivate ? PrivateMeetingTitle : m.Title, m.IsPrivate, m.Cancelled, m.Declined,
                counts ? minutes : 0, PlannerActuals.MeetingMinutes(minutes, m.Declined, m.Cancelled, ended: now >= m.EndAt));
        }).ToList();
        // An all-day meeting covers the whole day; it does not clash with each planned hour.
        var allDay = meetings.Where(m => m.IsAllDay).Select(m => m.TaskId).ToHashSet();
        var busy = meetingViews.Where(m => !m.Cancelled && !m.Declined && !allDay.Contains(m.TaskId)).ToList();

        var blockViews = blocks.Select(b =>
        {
            var task = tasks.GetValueOrDefault(b.TaskId);
            return new PlannerBlockView(b.Id, b.TaskId, task?.Title ?? string.Empty, b.StartAt, b.EndAt,
                (int)(b.EndAt - b.StartAt).TotalMinutes, actualByTask.GetValueOrDefault(b.TaskId),
                StillAssigned: task?.AssignedToEmployee ?? false,
                OverlapsMeeting: busy.Any(m => m.StartAt < b.EndAt && m.EndAt > b.StartAt));
        }).ToList();

        var planned = blockViews.Sum(b => b.PlannedMinutes) + meetingViews.Sum(m => m.PlannedMinutes);
        var actual = blocks.Select(b => b.TaskId).Distinct().Sum(id => actualByTask.GetValueOrDefault(id)) + meetingViews.Sum(m => m.ActualMinutes);
        return new PlannerDayResponse(day, zone.Id, editable, blockViews, meetingViews,
            due.Select(t => new PlannerDueTaskView(t.TaskId, t.Title, t.DueDate, t.EstimatedHours)).ToList(),
            new PlannerTotals(planned, actual), AttendanceMinutes(attendance, now));
    }

    /// <summary>A meeting's minutes on this day only: its overlap with the day, or one workday when an all-day meeting
    /// covers the day (MeetingPersonHours.AllDayMinutesPerDay).</summary>
    private static int MinutesInDay(DateTimeOffset meetingStart, DateTimeOffset meetingEnd, bool isAllDay, DateTimeOffset dayStart,
        DateTimeOffset dayEnd)
    {
        var from = meetingStart > dayStart ? meetingStart : dayStart;
        var to = meetingEnd < dayEnd ? meetingEnd : dayEnd;
        if (to <= from)
            return 0;
        return isAllDay ? MeetingPersonHours.AllDayMinutesPerDay : (int)Math.Round((to - from).TotalMinutes);
    }

    /// <summary>Worked minutes once clocked out; while still clocked in, the time since clock-in less breaks so far.</summary>
    private static int AttendanceMinutes(PlannerAttendance? attendance, DateTimeOffset now)
    {
        if (attendance is null)
            return 0;
        if (attendance.ActualStart is { } start && attendance.ActualEnd is null)
            return Math.Max(0, (int)Math.Round((now - start).TotalMinutes) - attendance.BreakMinutes);
        return attendance.WorkedMinutes;
    }

    private async Task<Zone?> ZoneOfAsync(Guid tenantId, Guid employeeId, CancellationToken ct)
    {
        var employee = (await _employees.ListByIdsAsync(tenantId, [employeeId], ct)).GetValueOrDefault(employeeId);
        if (employee is null)
            return null;
        var id = await _timezones.ResolveForUserAsync(tenantId, employee.UserId, ct);
        // A zone id this machine does not know (a typo in the profile or legal entity) plans in UTC instead of failing.
        return TimeZoneInfo.TryFindSystemTimeZoneById(id, out var info) ? new Zone(id, info) : new Zone("UTC", TimeZoneInfo.Utc);
    }

    private DateOnly TodayIn(TimeZoneInfo zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.UtcNow, zone).DateTime);

    private static DateOnly DayOf(DateTimeOffset instant, TimeZoneInfo zone)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

    private static (DateTimeOffset Start, DateTimeOffset End) Bounds(DateOnly day, TimeZoneInfo zone)
    {
        DateTimeOffset At(DateOnly d)
        {
            var local = d.ToDateTime(TimeOnly.MinValue);
            return new DateTimeOffset(local, zone.GetUtcOffset(local));
        }
        return (At(day), At(day.AddDays(1)));
    }

    private static Result<T> BeyondHorizon<T>() => Result<T>.Failure(
        $"You can plan up to {PlanningHorizonDays} days ahead.", 400, PlannerErrorCodes.BeyondHorizon);

    private static Result<T> Disabled<T>() => Result<T>.Failure("The planner is not available.", 503, PlannerErrorCodes.Disabled);

    private static Result<T> NotFound<T>() => Result<T>.Failure(NotFoundMessage, 404, PlannerErrorCodes.NotFound);
}
