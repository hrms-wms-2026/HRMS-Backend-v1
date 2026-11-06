namespace ONEVO.Application.Features.Monitoring.CheckIn.Queries.GetTrayDailyTasks;

using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.DTOs;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

/// <summary>
/// Tray Daily Summary: which tasks the employee clocked in on during their local work date,
/// how long, and where each task stands now. Identity comes only from the tray device token.
/// </summary>
public sealed class GetTrayDailyTasksQueryHandler(
    ITrayCurrentDevice device,
    IAttendanceTodayStateService todayState,
    IAttendanceReadRepository attendance,
    ITenantRepository tenants,
    ITenantContextSwitcher tenantSwitcher,
    ITaskClockingSessionRepository taskSessions,
    ITaskPercentageLogRepository percentageLogs)
    : IRequestHandler<GetTrayDailyTasksQuery, Result<TrayDailyTasksDto>>
{
    /// <summary>Keeps the tray IPC reply bounded on unusually busy days.</summary>
    public const int MaxTasks = 20;

    public async Task<Result<TrayDailyTasksDto>> Handle(GetTrayDailyTasksQuery request, CancellationToken ct)
    {
        if (!device.IsAuthenticated || device.TenantId == Guid.Empty || device.UserId == Guid.Empty)
            return Result<TrayDailyTasksDto>.Failure("A valid tray device token is required.", 401);

        var tenant = await tenants.GetByIdAsync(device.TenantId, ct);
        if (tenant is null)
            return Result<TrayDailyTasksDto>.Failure("Tenant not found.", 401);

        await tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);

        var contextResult = await todayState.ResolveContextAsync(device.TenantId, device.UserId, ct);
        if (!contextResult.IsSuccess)
            return Result<TrayDailyTasksDto>.Failure(contextResult.Error!, contextResult.StatusCode ?? 400);

        var context = contextResult.Value!;
        var windowStart = context.LocalDayWindow.Start.ToUniversalTime();
        var windowEnd = context.LocalDayWindow.End.ToUniversalTime();
        var now = context.UtcNow;

        // Once the workday is clocked out, a task session someone left open stops counting there
        // and no longer shows as running.
        var record = await attendance.GetRecordAsync(
            context.Employee.TenantId, context.Employee.Id, context.WorkDate, ct);
        var dayClosed = record?.ActualEnd is { } actualEnd && actualEnd <= now;
        var openSessionEnd = dayClosed ? record!.ActualEnd!.Value : now;

        var rows = await taskSessions.GetForEmployeeOverlappingAsync(
            context.Employee.TenantId, context.Employee.Id, windowStart, windowEnd, ct);

        var taskIds = rows.Select(row => row.TaskId).Distinct().ToList();
        var pushesByTask = (await percentageLogs.GetPushesByEmployeeAsync(
                context.Employee.TenantId, context.Employee.Id, taskIds, windowStart, windowEnd, ct))
            .GroupBy(log => log.TaskId)
            .ToDictionary(group => group.Key, group => group.OrderBy(log => log.ChangedAt).ToList());

        var tasks = rows
            .GroupBy(row => row.TaskId)
            .Select(group => BuildTask(
                group.ToList(), windowStart, windowEnd, openSessionEnd, dayClosed,
                pushesByTask.GetValueOrDefault(group.Key)))
            .OrderByDescending(task => task.IsClockedInNow)
            .ThenByDescending(task => task.LastActivityAtUtc)
            .ToList();

        return Result<TrayDailyTasksDto>.Success(new TrayDailyTasksDto(
            WorkDate: context.WorkDate,
            TotalMinutes: UnionMinutes(rows, windowStart, windowEnd, openSessionEnd),
            TaskCount: tasks.Count,
            DoneCount: tasks.Count(task => task.IsDone),
            Tasks: tasks.Take(MaxTasks).ToList()));
    }

    private static TrayDailyTaskDto BuildTask(
        IReadOnlyList<EmployeeTaskSessionRow> sessions,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        DateTimeOffset openSessionEnd,
        bool dayClosed,
        IReadOnlyList<TaskPercentageLog>? pushesToday)
    {
        var task = sessions[0];
        var totalSeconds = 0d;
        var lastActivity = DateTimeOffset.MinValue;

        foreach (var session in sessions)
        {
            if (Clip(session, windowStart, windowEnd, openSessionEnd) is var (start, end))
                totalSeconds += (end - start).TotalSeconds;

            var activity = session.ClockOutAt ?? openSessionEnd;
            if (activity > lastActivity)
                lastActivity = activity;
        }

        // The status pill and the done flag must agree, so the status decides; CompletedAt is
        // only a fallback when the status row is missing.
        var isDone = task.StatusCategory is null
            ? task.TaskCompletedAt is not null
            : task.StatusMarksTaskComplete || task.StatusCategory == TaskStatusCategories.Done;

        return new TrayDailyTaskDto(
            TaskId: task.TaskId,
            ShortId: task.ShortId,
            Title: task.Title,
            ProjectName: task.ProjectName,
            StatusName: task.StatusName,
            StatusColor: task.StatusColor,
            StatusCategory: task.StatusCategory,
            ProgressPercent: isDone ? 100 : Math.Clamp(task.ProgressPercent, 0, 100),
            MinutesToday: (int)Math.Round(totalSeconds / 60d),
            SessionCount: sessions.Count,
            IsDone: isDone,
            IsClockedInNow: !dayClosed && sessions.Any(session => session.ClockOutAt is null),
            FirstClockInAtUtc: sessions.Min(session => session.ClockInAt),
            LastActivityAtUtc: lastActivity,
            TodayFromPercent: pushesToday is { Count: > 0 } ? Math.Clamp(pushesToday[0].PreviousPercent, 0, 100) : null,
            TodayToPercent: pushesToday is { Count: > 0 } ? Math.Clamp(pushesToday[^1].NewPercent, 0, 100) : null);
    }

    /// <summary>Wall-clock minutes covered by any task session, so tasks clocked in at the same
    /// time are not counted twice in the day total.</summary>
    private static int UnionMinutes(
        IReadOnlyList<EmployeeTaskSessionRow> sessions,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        DateTimeOffset openSessionEnd)
    {
        var intervals = sessions
            .Select(session => Clip(session, windowStart, windowEnd, openSessionEnd))
            .OfType<(DateTimeOffset Start, DateTimeOffset End)>()
            .OrderBy(interval => interval.Start)
            .ToList();

        var totalSeconds = 0d;
        DateTimeOffset? runStart = null, runEnd = null;
        foreach (var (start, end) in intervals)
        {
            if (runEnd is not null && start <= runEnd)
            {
                if (end > runEnd)
                    runEnd = end;
                continue;
            }

            if (runStart is not null)
                totalSeconds += (runEnd!.Value - runStart.Value).TotalSeconds;
            runStart = start;
            runEnd = end;
        }

        if (runStart is not null)
            totalSeconds += (runEnd!.Value - runStart.Value).TotalSeconds;

        return (int)Math.Round(totalSeconds / 60d);
    }

    /// <summary>The part of a session inside the local work date, with open sessions ending at
    /// <paramref name="openSessionEnd"/>. Null when nothing of it falls inside.</summary>
    private static (DateTimeOffset Start, DateTimeOffset End)? Clip(
        EmployeeTaskSessionRow session,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        DateTimeOffset openSessionEnd)
    {
        var end = session.ClockOutAt ?? openSessionEnd;
        var start = session.ClockInAt > windowStart ? session.ClockInAt : windowStart;
        if (end > windowEnd)
            end = windowEnd;
        return end > start ? (start, end) : null;
    }
}
