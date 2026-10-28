using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Planner.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Planner.Services;

namespace ONEVO.Application.Features.WorkManagement.Planner.TeamSchedule;

/// <summary>One person's day as the composer needs it. Blocks, sessions and meetings may reach outside the day; they are
/// clipped here. Blocks are empty when the Planner is off.</summary>
public sealed record TeamScheduleInput(
    Guid EmployeeId, string DisplayName, Guid? AvatarFileId, string Timezone, DateTimeOffset DayStart, DateTimeOffset DayEnd,
    string? PresenceStatus, IReadOnlyList<TeamBlockRow> Blocks, IReadOnlyList<TeamSessionRow> Sessions,
    IReadOnlyList<EmployeeDayMeeting> Meetings, IReadOnlyDictionary<Guid, TeamTaskRow> Tasks);

/// <summary>Today's Team Schedule rules (2026-10-29 design, sections 6.2 and 6.3) for one person, pure. The person is always
/// someone other than the viewer, so private meeting titles are always masked.</summary>
public static class TeamScheduleComposer
{
    public const string PrivateMeetingTitle = "Private meeting";

    public static TeamScheduleMember Compose(TeamScheduleInput input, DateTimeOffset now, bool plannerEnabled)
    {
        var (dayStart, dayEnd) = (input.DayStart, input.DayEnd);
        var blocks = plannerEnabled
            ? input.Blocks.Where(b => b.Start < dayEnd && b.End > dayStart).OrderBy(b => b.Start).ToList()
            : [];
        var meetings = input.Meetings.Where(m => !m.Declined && m.StartAt < dayEnd && m.EndAt > dayStart).OrderBy(m => m.StartAt).ToList();
        var sessions = input.Sessions;
        string TaskTitle(Guid taskId) => input.Tasks.TryGetValue(taskId, out var task) ? task.Title : string.Empty;
        string MeetingTitle(EmployeeDayMeeting m) => m.IsPrivate ? PrivateMeetingTitle : m.Title;

        var attribution = TaskTimeAttribution.Attribute(
            blocks.Select(b => new AttributionBlock(b.TaskId, b.Start, b.End)),
            sessions.Select(s => new AttributionSession(s.TaskId, s.ClockIn, s.ClockOut)),
            dayStart, dayEnd, now);

        var openTasks = sessions.Where(s => s.ClockOut is null && s.ClockIn <= now).Select(s => s.TaskId).ToHashSet();
        bool ClockedInside(TeamBlockRow b) => sessions.Any(s => s.TaskId == b.TaskId && s.ClockIn < b.End && (s.ClockOut ?? now) > b.Start);

        var items = new List<TeamScheduleItem>();
        foreach (var b in blocks)
            items.Add(new TeamScheduleItem("task", b.TaskId, TaskTitle(b.TaskId), b.Start, b.End, BlockStatus(b), false));
        foreach (var m in meetings)
            items.Add(new TeamScheduleItem("meeting", m.TaskId, MeetingTitle(m), m.StartAt, m.EndAt, MeetingStatus(m), m.IsPrivate));
        items = items.OrderBy(i => i.Start).ThenBy(i => i.Kind).ToList();

        string BlockStatus(TeamBlockRow b)
        {
            var task = input.Tasks.GetValueOrDefault(b.TaskId);
            if (task?.IsComplete == true)
                return "completed";
            if (openTasks.Contains(b.TaskId))
                return "in_progress";
            if (b.Start <= now && !ClockedInside(b))
                return "scheduled_not_clocked";
            return task?.ProgressPercent > 0 || ClockedInside(b) ? "in_progress" : "pending";
        }

        string MeetingStatus(EmployeeDayMeeting m)
            => m.Cancelled ? "cancelled" : now < m.StartAt ? "upcoming" : now < m.EndAt ? "in_meeting" : "finished";

        var countedMeetings = meetings.Where(m => !m.Cancelled).ToList();
        var plannedMeetingMinutes = countedMeetings.Sum(m => PlannerActuals.MeetingMinutesInDay(m.StartAt, m.EndAt, m.IsAllDay, dayStart, dayEnd));

        // Score: items due by now; a meeting is done once it has ended.
        var dueTasks = items.Where(i => i.Kind == "task" && i.Start <= now).ToList();
        var dueMeetings = countedMeetings.Where(m => m.StartAt <= now).ToList();
        var due = dueTasks.Count + dueMeetings.Count;
        var done = dueTasks.Count(i => i.Status == "completed") + dueMeetings.Count(m => now >= m.EndAt);
        var elapsedEnd = now < dayEnd ? now : dayEnd;
        var elapsedPlanned = (int)Math.Round(blocks.Sum(b =>
        {
            var from = b.Start > dayStart ? b.Start : dayStart;
            var to = b.End < elapsedEnd ? b.End : elapsedEnd;
            return to > from ? (to - from).TotalMinutes : 0;
        }), MidpointRounding.AwayFromZero);
        var score = ExecutionScore.Compute(due, done, elapsedPlanned, attribution.ScheduledMinutes,
            onLeave: input.PresenceStatus == "on_leave", plannerEnabled);

        var unscheduled = plannerEnabled
            ? attribution.Unscheduled.Select(s => new TeamScheduleUnscheduled(s.TaskId, TaskTitle(s.TaskId), s.Start, s.End, s.Minutes,
                s.Kind == UnscheduledKind.NoPlan ? "no_plan" : "outside_slot")).ToList()
            : [];

        var conflicts = new List<TeamScheduleConflict>();
        foreach (var b in blocks)
        {
            foreach (var m in countedMeetings.Where(m => !m.IsAllDay && m.StartAt < b.End && m.EndAt > b.Start))
            {
                conflicts.Add(new TeamScheduleConflict("block_meeting", Max(b.Start, m.StartAt), Min(b.End, m.EndAt),
                    TaskTitle(b.TaskId), MeetingTitle(m)));
            }
        }
        var clipped = sessions
            .Select(s => (s.TaskId, Start: Max(s.ClockIn, dayStart), End: Min(s.ClockOut ?? now, Min(now, dayEnd))))
            .Where(s => s.End > s.Start)
            .ToList();
        for (var i = 0; i < clipped.Count; i++)
        {
            for (var j = i + 1; j < clipped.Count; j++)
            {
                var (a, c) = (clipped[i], clipped[j]);
                if (a.TaskId == c.TaskId || a.Start >= c.End || c.Start >= a.End)
                    continue;
                var (first, second) = a.Start <= c.Start ? (a, c) : (c, a);
                conflicts.Add(new TeamScheduleConflict("parallel_clock", Max(a.Start, c.Start), Min(a.End, c.End),
                    TaskTitle(first.TaskId), TaskTitle(second.TaskId)));
            }
        }

        return new TeamScheduleMember(input.EmployeeId, input.DisplayName, input.AvatarFileId, input.Timezone, input.PresenceStatus,
            attribution.PlannedMinutes, plannedMeetingMinutes, attribution.ClockedMinutes,
            plannerEnabled ? attribution.ScheduledMinutes : 0,
            plannerEnabled ? attribution.OutsideSlotMinutes : 0,
            plannerEnabled ? attribution.NoPlanMinutes : 0,
            score.ItemCompletionPercent, score.TimeCoveragePercent, score.Score, WireReason(score.Reason), Rank: null,
            items, unscheduled, conflicts.OrderBy(c => c.Start).ToList());
    }

    /// <summary>Counts over the composed members; a task counts once per person, by its best status.</summary>
    public static TeamScheduleSummary Summarize(IReadOnlyList<TeamScheduleMember> members)
    {
        int planned = 0, completed = 0, inProgress = 0, pending = 0;
        foreach (var m in members)
        {
            foreach (var task in m.Items.Where(i => i.Kind == "task").GroupBy(i => i.TaskId))
            {
                planned++;
                if (task.Any(i => i.Status == "completed")) completed++;
                else if (task.Any(i => i.Status == "in_progress")) inProgress++;
                else pending++;
            }
        }
        return new TeamScheduleSummary(members.Count, planned, completed, inProgress, pending,
            members.Sum(m => m.Items.Count(i => i.Kind == "meeting" && i.Status != "cancelled")),
            members.Count(m => m.OutsideSlotMinutes + m.NoPlanMinutes > 0),
            members.Sum(m => m.OutsideSlotMinutes + m.NoPlanMinutes),
            members.Sum(m => m.Conflicts.Count));
    }

    /// <summary>Ranked order (ExecutionScore.Rank); scored rows get 1..n.</summary>
    public static IReadOnlyList<TeamScheduleMember> Rank(IEnumerable<TeamScheduleMember> members)
    {
        var ranked = ExecutionScore.Rank(members,
            m => new ExecutionScoreResult(m.ItemCompletionPercent, m.TimeCoveragePercent, m.Score, NotRankedReason.None),
            m => m.DisplayName);
        var rank = 0;
        return ranked.Select(m => m.Score is null ? m : m with { Rank = ++rank }).ToList();
    }

    public static string WireReason(NotRankedReason reason) => reason switch
    {
        NotRankedReason.NoItemsYet => "no_items_yet",
        NotRankedReason.NoPlannedTaskTime => "no_planned_task_time",
        NotRankedReason.OnLeave => "on_leave",
        NotRankedReason.PlannerOff => "planner_off",
        _ => "none",
    };

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
