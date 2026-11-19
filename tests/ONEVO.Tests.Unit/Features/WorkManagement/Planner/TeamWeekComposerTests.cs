using ONEVO.Application.Features.WorkManagement.Planner.TeamSchedule;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Planner;

/// <summary>Team week planned vs actual (2026-11-02 design match, section 7.4): summing composed person-days into the week.</summary>
public class TeamWeekComposerTests
{
    private static readonly DateOnly Monday = new(2026, 10, 26);
    private static readonly DateTimeOffset AsOf = new(2026, 10, 28, 6, 30, 0, TimeSpan.Zero);

    private static TeamScheduleMember Day(int plannedTask = 0, int plannedMeeting = 0, int clocked = 0, int scheduled = 0, int outside = 0,
        int noPlan = 0)
        => new(Guid.NewGuid(), "x", null, "UTC", null, plannedTask, plannedMeeting, clocked, scheduled, outside, noPlan,
            null, null, null, "none", null, [], [], [], null, []);

    private static TeamWeekPersonInput Person(string name, IEnumerable<(DateOnly, TeamScheduleMember)> days, int plannedElapsed = 0,
        int prevPlannedElapsed = 0, int prevScheduled = 0, int prevClocked = 0, int prevUnscheduled = 0)
        => new(Guid.NewGuid(), name, null, days.ToList(), plannedElapsed, prevPlannedElapsed, prevScheduled, prevClocked, prevUnscheduled);

    private static IEnumerable<(DateOnly, TeamScheduleMember)> Week(Func<int, TeamScheduleMember> day)
        => Enumerable.Range(0, 7).Select(i => (Monday.AddDays(i), day(i)));

    [Fact]
    public void Sums_days_into_the_seven_week_slots()
    {
        var a = Person("A", Week(i => Day(plannedTask: 60 * i, plannedMeeting: 10, clocked: 30, scheduled: 20, outside: 5, noPlan: 5)));
        var b = Person("B", Week(i => Day(plannedTask: 1, clocked: i)));

        var week = TeamWeekComposer.Compose(Monday, true, AsOf, [a, b]);

        Assert.Equal(7, week.Days.Count);
        Assert.Equal(Monday, week.WeekStart);
        Assert.Equal(Monday.AddDays(6), week.WeekEnd);
        for (var i = 0; i < 7; i++)
        {
            var d = week.Days[i];
            Assert.Equal(Monday.AddDays(i), d.Date);
            Assert.Equal(60 * i + 1, d.PlannedTaskMinutes);
            Assert.Equal(10, d.PlannedMeetingMinutes);
            Assert.Equal(30 + i, d.ClockedMinutes);
            Assert.Equal(20, d.ScheduledMinutes);
            Assert.Equal(5, d.OutsideSlotMinutes);
            Assert.Equal(5, d.NoPlanMinutes);
        }
        Assert.Equal(60 * 21 + 7 + 70, week.Totals.PlannedMinutes);
        Assert.Equal(210 + 21, week.Totals.ClockedMinutes);
        Assert.Equal(140, week.Totals.ScheduledMinutes);
        Assert.Equal(70, week.Totals.UnscheduledMinutes);
    }

    [Fact]
    public void Person_local_dates_place_minutes_on_their_own_weekday()
    {
        // A UTC-05:00 person's Tuesday arrives already dated Tuesday by the handler; a day outside the week is ignored.
        var p = Person("NY", [(Monday.AddDays(1), Day(clocked: 45)), (Monday.AddDays(-1), Day(clocked: 999)), (Monday.AddDays(7), Day(clocked: 999))]);

        var week = TeamWeekComposer.Compose(Monday, true, AsOf, [p]);

        Assert.Equal(45, week.Days[1].ClockedMinutes);
        Assert.Equal(45, week.Days.Sum(d => d.ClockedMinutes));
    }

    [Fact]
    public void Coverage_uses_elapsed_plan()
    {
        var p = Person("A", [(Monday, Day(plannedTask: 600, scheduled: 120, clocked: 120))], plannedElapsed: 240);

        var week = TeamWeekComposer.Compose(Monday, true, AsOf, [p]);

        Assert.Equal(50.0m, week.Totals.CoveragePercent);
        Assert.Equal(50.0m, Assert.Single(week.Members).CoveragePercent);
        Assert.Equal(600, week.Totals.PlannedMinutes);
    }

    [Fact]
    public void Coverage_is_capped_and_rounded_half_away_from_zero()
    {
        var capped = Person("A", [(Monday, Day(scheduled: 300))], plannedElapsed: 240);
        var rounded = Person("B", [(Monday, Day(scheduled: 1))], plannedElapsed: 1600);   // 0.0625% -> 0.1

        Assert.Equal(100m, Assert.Single(TeamWeekComposer.Compose(Monday, true, AsOf, [capped]).Members).CoveragePercent);
        Assert.Equal(0.1m, Assert.Single(TeamWeekComposer.Compose(Monday, true, AsOf, [rounded]).Members).CoveragePercent);
    }

    [Fact]
    public void Coverage_null_without_elapsed_plan()
    {
        var p = Person("A", [(Monday, Day(plannedTask: 600, clocked: 60))], plannedElapsed: 0);

        var week = TeamWeekComposer.Compose(Monday, true, AsOf, [p]);

        Assert.Null(week.Totals.CoveragePercent);
        Assert.Null(Assert.Single(week.Members).CoveragePercent);
    }

    [Fact]
    public void Previous_totals_come_from_previous_fields()
    {
        var a = Person("A", [], prevPlannedElapsed: 200, prevScheduled: 100, prevClocked: 150, prevUnscheduled: 50);
        var b = Person("B", [], prevPlannedElapsed: 200, prevScheduled: 0, prevClocked: 10, prevUnscheduled: 10);

        var week = TeamWeekComposer.Compose(Monday, true, AsOf, [a, b]);

        Assert.Equal(new TeamWeekTotals(400, 160, 100, 60, 25.0m), week.Previous);
    }

    [Fact]
    public void Members_ordered_by_planned_then_clocked_then_name()
    {
        var low = Person("Zed", [(Monday, Day(plannedTask: 60, clocked: 10))]);
        var tieB = Person("Bea", [(Monday, Day(plannedTask: 120, clocked: 30))]);
        var tieA = Person("Ann", [(Monday, Day(plannedTask: 120, clocked: 30))]);
        var moreClocked = Person("Cid", [(Monday, Day(plannedTask: 120, clocked: 90))]);

        var week = TeamWeekComposer.Compose(Monday, true, AsOf, [low, tieB, tieA, moreClocked]);

        Assert.Equal(["Cid", "Ann", "Bea", "Zed"], week.Members.Select(m => m.DisplayName));
    }

    [Fact]
    public void Planner_off_zeroes_plan_and_keeps_clocked()
    {
        var p = Person("A", [(Monday, Day(plannedTask: 60, plannedMeeting: 30, clocked: 90, scheduled: 40, outside: 20, noPlan: 30))],
            plannedElapsed: 60, prevPlannedElapsed: 60, prevScheduled: 30, prevClocked: 70, prevUnscheduled: 40);

        var week = TeamWeekComposer.Compose(Monday, false, AsOf, [p]);

        Assert.False(week.PlannerEnabled);
        Assert.Equal(new TeamWeekDay(Monday, 0, 0, 90, 0, 0, 0), week.Days[0]);
        Assert.Equal(new TeamWeekTotals(0, 90, 0, 0, null), week.Totals);
        Assert.Equal(new TeamWeekTotals(0, 70, 0, 0, null), week.Previous);
        var member = Assert.Single(week.Members);
        Assert.Equal((0, 90, 0, 0, (decimal?)null),
            (member.PlannedMinutes, member.ClockedMinutes, member.ScheduledMinutes, member.UnscheduledMinutes, member.CoveragePercent));
    }
}
