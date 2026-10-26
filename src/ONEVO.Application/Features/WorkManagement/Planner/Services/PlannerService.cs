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
/// Day planner (2026-10-26 design, Part B). An employee plans only today, in their own timezone, as task blocks next to
/// their meetings; a block never touches the task. Their reporting-line managers (any ancestor) read any past day or
/// today, with private meetings masked; anyone else gets 404.
/// </summary>
public sealed class PlannerService
{
    public const int MinimumBlockMinutes = 15;
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
        if (day > today)
            return Result<PlannerDayResponse>.Failure("Only today and earlier days can be shown.", 400, PlannerErrorCodes.NotToday);

        return Result<PlannerDayResponse>.Success(
            await ComposeAsync(tenantId, employeeId, day, zone.Value, editable: own && day == today, maskPrivate: !own, ct));
    }

    public async Task<Result<PlannerDayResponse>> CreateBlockAsync(Guid tenantId, Guid callerEmployeeId, CreatePlannedBlockRequest request,
        CancellationToken ct)
    {
        var check = await CheckWriteAsync(tenantId, callerEmployeeId, request.StartAt, request.EndAt, exceptBlockId: null, ct);
        if (check.Failure is { } failure)
            return failure;
        var task = (await _planner.GetTasksAsync(tenantId, callerEmployeeId, [request.TaskId], ct)).GetValueOrDefault(request.TaskId);
        if (task is null || task.IsMeeting || !task.AssignedToEmployee)
            return Result<PlannerDayResponse>.Failure("Only a task assigned to you can be planned.", 400, PlannerErrorCodes.TaskNotPlannable);

        _planner.AddBlock(new PlannedTaskBlock
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = callerEmployeeId, TaskId = request.TaskId,
            StartAt = request.StartAt.ToUniversalTime(), EndAt = request.EndAt.ToUniversalTime(), CreatedById = callerEmployeeId,
        });
        await _unitOfWork.SaveChangesAsync(ct);
        return Result<PlannerDayResponse>.Success(await ComposeTodayAsync(tenantId, callerEmployeeId, check.Zone, ct));
    }

    public async Task<Result<PlannerDayResponse>> MoveBlockAsync(Guid tenantId, Guid callerEmployeeId, Guid blockId,
        MovePlannedBlockRequest request, CancellationToken ct)
    {
        if (!_options.Value.Enabled)
            return Disabled<PlannerDayResponse>();
        var block = await _planner.GetBlockTrackedAsync(tenantId, callerEmployeeId, blockId, ct);
        if (block is null)
            return NotFound<PlannerDayResponse>();
        var check = await CheckWriteAsync(tenantId, callerEmployeeId, request.StartAt, request.EndAt, blockId, ct);
        if (check.Failure is { } failure)
            return failure;
        if (!IsOn(TodayIn(check.Zone.Info), block.StartAt, check.Zone.Info))
            return Result<PlannerDayResponse>.Failure("Only today's plan can change.", 400, PlannerErrorCodes.NotToday);

        block.StartAt = request.StartAt.ToUniversalTime();
        block.EndAt = request.EndAt.ToUniversalTime();
        block.UpdatedAt = _clock.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);
        return Result<PlannerDayResponse>.Success(await ComposeTodayAsync(tenantId, callerEmployeeId, check.Zone, ct));
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
        if (!IsOn(TodayIn(zone.Value.Info), block.StartAt, zone.Value.Info))
            return Result<PlannerDayResponse>.Failure("Only today's plan can change.", 400, PlannerErrorCodes.NotToday);

        _planner.RemoveBlock(block);
        await _unitOfWork.SaveChangesAsync(ct);
        return Result<PlannerDayResponse>.Success(await ComposeTodayAsync(tenantId, callerEmployeeId, zone.Value, ct));
    }

    private readonly record struct Zone(string Id, TimeZoneInfo Info);

    private readonly record struct WriteCheck(Zone Zone, Result<PlannerDayResponse>? Failure);

    /// <summary>Flag on, a valid block on the caller's today, no overlap with their other blocks.</summary>
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
        var today = TodayIn(zone.Value.Info);
        if (!IsOn(today, start, zone.Value.Info) || !IsOn(today, end.AddTicks(-1), zone.Value.Info))
            return new(zone.Value, Result<PlannerDayResponse>.Failure("Only today can be planned.", 400, PlannerErrorCodes.NotToday));
        if (await _planner.OverlapsAsync(tenantId, employeeId, start, end, exceptBlockId, ct))
            return new(zone.Value, Result<PlannerDayResponse>.Failure("This overlaps another planned block.", 409, PlannerErrorCodes.BlockOverlap));
        return new(zone.Value, null);
    }

    private Task<PlannerDayResponse> ComposeTodayAsync(Guid tenantId, Guid employeeId, Zone zone, CancellationToken ct)
        => ComposeAsync(tenantId, employeeId, TodayIn(zone.Info), zone, editable: true, maskPrivate: false, ct);

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
        var busy = meetingViews.Where(m => !m.Cancelled && !m.Declined).ToList();

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
        return new Zone(id, TimeZoneInfo.FindSystemTimeZoneById(id));
    }

    private DateOnly TodayIn(TimeZoneInfo zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.UtcNow, zone).DateTime);

    private static bool IsOn(DateOnly day, DateTimeOffset instant, TimeZoneInfo zone)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime) == day;

    private static (DateTimeOffset Start, DateTimeOffset End) Bounds(DateOnly day, TimeZoneInfo zone)
    {
        DateTimeOffset At(DateOnly d)
        {
            var local = d.ToDateTime(TimeOnly.MinValue);
            return new DateTimeOffset(local, zone.GetUtcOffset(local));
        }
        return (At(day), At(day.AddDays(1)));
    }

    private static Result<T> Disabled<T>() => Result<T>.Failure("The planner is not available.", 503, PlannerErrorCodes.Disabled);

    private static Result<T> NotFound<T>() => Result<T>.Failure(NotFoundMessage, 404, PlannerErrorCodes.NotFound);
}
