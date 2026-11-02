using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Planner.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Planner.TeamSchedule;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Planner;

/// <summary>Today's Team Schedule (2026-10-29 design, sections 6.2 and 6.3): one report's day.</summary>
public class TeamScheduleComposerTests
{
    private static readonly DateTimeOffset DayStart = new(2026, 11, 2, 0, 0, 0, TimeSpan.FromHours(5.5));
    private static readonly Guid Employee = Guid.NewGuid();
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    private static DateTimeOffset At(int hour, int minute = 0) => DayStart.AddHours(hour).AddMinutes(minute);

    private static TeamBlockRow Block(Guid task, int from, int to) => new(Employee, task, At(from), At(to));

    private static TeamSessionRow Session(Guid task, DateTimeOffset start, DateTimeOffset? end) => new(Employee, task, start, end);

    private static EmployeeDayMeeting Meeting(int from, int to, string title = "Standup", bool isPrivate = false, bool cancelled = false,
        bool allDay = false) => new(Guid.NewGuid(), Guid.NewGuid(), At(from), allDay ? DayStart : At(from), allDay ? DayStart.AddDays(1) : At(to),
        allDay, title, isPrivate, cancelled, Declined: false);

    private static Dictionary<Guid, TeamTaskRow> Tasks(bool aComplete = false) => new()
    {
        [A] = new TeamTaskRow(A, "T-A", "Write report", null, null, aComplete, aComplete ? 100 : 0),
        [B] = new TeamTaskRow(B, "T-B", "Fix bug", null, null, false, 0),
    };

    private static TeamScheduleMember Compose(TeamBlockRow[] blocks, TeamSessionRow[] sessions, EmployeeDayMeeting[]? meetings = null,
        DateTimeOffset? now = null, bool plannerEnabled = true, Dictionary<Guid, TeamTaskRow>? tasks = null, string? presence = null)
        => TeamScheduleComposer.Compose(new TeamScheduleInput(Employee, "Priya K", null, "Asia/Colombo", DayStart, DayStart.AddDays(1),
            presence, blocks, sessions, meetings ?? [], tasks ?? Tasks()), now ?? At(18), plannerEnabled);

    [Fact]
    public void No_plan_worker_is_listed_with_unscheduled_and_not_ranked()
    {
        var m = Compose([], [Session(B, At(9), At(11))]);

        Assert.Equal((120, 0, 0, 120), (m.ClockedMinutes, m.ScheduledMinutes, m.OutsideSlotMinutes, m.NoPlanMinutes));
        var segment = Assert.Single(m.Unscheduled);
        Assert.Equal(("no_plan", "Fix bug", 120), (segment.Kind, segment.Title, segment.Minutes));
        Assert.Null(m.Score);
        Assert.Null(m.ItemCompletionPercent);
        Assert.Equal("no_items_yet", m.NotRankedReason);
    }

    [Fact]
    public void Block_10_to_11_clock_1020_to_1120_gives_40_and_20()
    {
        var m = Compose([Block(A, 10, 11)], [Session(A, At(10, 20), At(11, 20))]);

        Assert.Equal((60, 60, 40, 20, 0), (m.PlannedTaskMinutes, m.ClockedMinutes, m.ScheduledMinutes, m.OutsideSlotMinutes, m.NoPlanMinutes));
        Assert.Equal(66.7m, m.TimeCoveragePercent);   // 40 of 60 planned minutes
    }

    [Fact]
    public void Private_meeting_title_is_masked()
    {
        var m = Compose([Block(A, 10, 11)], [], [Meeting(10, 11, "Salary review", isPrivate: true)]);

        var meeting = Assert.Single(m.Items, i => i.Kind == "meeting");
        Assert.Equal(("Private meeting", true), (meeting.Title, meeting.IsPrivate));
        Assert.Equal("Private meeting", Assert.Single(m.Conflicts).SecondTitle);
        Assert.DoesNotContain(m.Items, i => i.Title.Contains("Salary"));
    }

    [Fact]
    public void Cancelled_meeting_shown_but_not_counted()
    {
        var m = Compose([], [], [Meeting(9, 10, cancelled: true), Meeting(11, 12)], now: At(13));

        Assert.Equal(2, m.Items.Count);
        Assert.Equal("cancelled", m.Items[0].Status);
        Assert.Equal("finished", m.Items[1].Status);
        Assert.Equal(60, m.PlannedMeetingMinutes);
        Assert.Equal(1, TeamScheduleComposer.Summarize([m]).Meetings);
    }

    [Fact]
    public void Future_items_do_not_lower_score()
    {
        var m = Compose([Block(A, 9, 10), Block(B, 15, 16)], [Session(A, At(9), At(10))], now: At(12), tasks: Tasks(aComplete: true));

        Assert.Equal(100m, m.ItemCompletionPercent);
        Assert.Equal(100m, m.TimeCoveragePercent);
        Assert.Equal(100m, m.Score);
        Assert.Equal("pending", m.Items.Single(i => i.TaskId == B).Status);
    }

    [Fact]
    public void Started_block_without_clock_is_scheduled_not_clocked()
    {
        var m = Compose([Block(A, 9, 10)], [], now: At(9, 30));

        Assert.Equal("scheduled_not_clocked", Assert.Single(m.Items).Status);
        Assert.Equal(0m, m.ItemCompletionPercent);
    }

    [Fact]
    public void Block_meeting_overlap_is_a_conflict_but_all_day_is_not()
    {
        var m = Compose([Block(A, 10, 11)], [], [Meeting(10, 10, allDay: true), Meeting(10, 11, "Sync")]);

        var conflict = Assert.Single(m.Conflicts);
        Assert.Equal(("block_meeting", At(10), At(11), "Write report", "Sync"),
            (conflict.Kind, conflict.Start, conflict.End, conflict.FirstTitle, conflict.SecondTitle));
    }

    [Fact]
    public void Parallel_clocks_on_two_tasks_are_a_conflict()
    {
        var m = Compose([], [Session(A, At(9), At(10)), Session(B, At(9, 30), At(10, 30))]);

        var conflict = Assert.Single(m.Conflicts);
        Assert.Equal(("parallel_clock", At(9, 30), At(10)), (conflict.Kind, conflict.Start, conflict.End));
    }

    [Fact]
    public void Planner_off_keeps_clocked_minutes_but_no_plan_figures()
    {
        var m = Compose([Block(A, 10, 11)], [Session(A, At(10), At(11))], plannerEnabled: false);

        Assert.Equal(60, m.ClockedMinutes);
        Assert.Equal((0, 0, 0, 0), (m.PlannedTaskMinutes, m.ScheduledMinutes, m.OutsideSlotMinutes, m.NoPlanMinutes));
        Assert.Empty(m.Unscheduled);
        Assert.DoesNotContain(m.Items, i => i.Kind == "task");
        Assert.Equal("planner_off", m.NotRankedReason);
    }

    [Fact]
    public void Leave_is_not_ranked()
    {
        var m = Compose([Block(A, 9, 10)], [Session(A, At(9), At(10))], presence: "on_leave");

        Assert.Null(m.Score);
        Assert.Equal("on_leave", m.NotRankedReason);
    }

    [Fact]
    public void Rank_numbers_scored_rows_only()
    {
        var top = Compose([Block(A, 9, 10)], [Session(A, At(9), At(10))], tasks: Tasks(aComplete: true)) with { DisplayName = "Zed" };
        var low = Compose([Block(A, 9, 10)], []) with { DisplayName = "Amy" };
        var unranked = Compose([], []) with { DisplayName = "Bea" };

        var ranked = TeamScheduleComposer.Rank([unranked, low, top]);

        Assert.Equal(new[] { ("Zed", (int?)1), ("Amy", 2), ("Bea", null) }, ranked.Select(r => (r.DisplayName, r.Rank)));
    }

    // ---- Coverage audit (2026-10-29 plan, Task 12) ----

    [Fact]
    public void A_declined_meeting_is_absent_and_counts_nowhere()
    {
        var declined = Meeting(10, 11, "Board") with { Declined = true };

        var m = Compose([], [], [declined]);

        Assert.Empty(m.Items);
        Assert.Equal(0, m.PlannedMeetingMinutes);
        Assert.Equal(0, TeamScheduleComposer.Summarize([m]).Meetings);
    }

    [Fact]
    public void A_private_meeting_title_never_reaches_any_field()
    {
        var m = Compose([Block(A, 10, 11)], [], [Meeting(10, 11, "Salary review", isPrivate: true)]);

        var everything = string.Join("|", m.Items.Select(i => i.Title).Concat(m.Conflicts.SelectMany(c => new[] { c.FirstTitle, c.SecondTitle })));
        Assert.DoesNotContain("Salary", everything);
    }
}
