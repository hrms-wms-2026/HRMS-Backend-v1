using ONEVO.Application.Features.WorkManagement.Leadership.DTOs;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using ONEVO.Application.Features.WorkManagement.Planner.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Planner.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Leadership;

/// <summary>Team Progress planned vs worked (2026-10-29 design, section 6.1): windows, buckets, coverage and workload.</summary>
public sealed class LedTimeProgressCalculatorTests
{
    // Wednesday 2026-10-21 18:00 UTC; the week is Mon 19 .. Sun 25.
    private static readonly DateOnly Monday = new(2026, 10, 19);
    private static readonly DateOnly Today = new(2026, 10, 21);
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 18, 0, 0, TimeSpan.Zero);
    private static readonly Guid TaskA = Guid.NewGuid();

    private static DateTimeOffset On(DateOnly day, int hour) => new(day.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero);

    private static LedTimePerson Person(string name, AttributionBlock[] blocks, AttributionSession[] sessions, int? expected = null)
        => new(Guid.NewGuid(), name, TimeZoneInfo.Utc, expected, blocks, sessions);

    private static LedTimeProgressResponse Week(params LedTimePerson[] people)
        => LedTimeProgressCalculator.Build(LedPeriod.Week, Monday, Monday.AddDays(6), Today, Now, plannerEnabled: true, people,
            new Dictionary<Guid, TeamTaskRow> { [TaskA] = new(TaskA, "T-1", "Write report", null, null, false, 0) });

    [Fact]
    public void Previous_week_is_same_elapsed_span()
        => Assert.Equal((new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 14)),
            LedTimeProgressCalculator.PreviousSameElapsed(LedPeriod.Week, Monday, Today));

    [Fact]
    public void Previous_month_clamps_day_31_to_30()
    {
        Assert.Equal((new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)),
            LedTimeProgressCalculator.PreviousSameElapsed(LedPeriod.Month, new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 31)));
        Assert.Equal((new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)),
            LedTimeProgressCalculator.PreviousSameElapsed(LedPeriod.Month, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)));
        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 15)),
            LedTimeProgressCalculator.PreviousSameElapsed(LedPeriod.Month, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 15)));
    }

    [Fact]
    public void Unplanned_90_minutes_show_as_no_plan_and_touch_no_allocation()
    {
        var r = Week(Person("Ada", [], [new AttributionSession(TaskA, On(Today, 9), On(Today, 9).AddMinutes(90))]));

        Assert.Equal((90, 0, 0, 90), (r.ClockedMinutes, r.ScheduledMinutes, r.OutsideSlotMinutes, r.NoPlanMinutes));
        var row = Assert.Single(r.Unscheduled);
        Assert.Equal(("no_plan", "T-1", "Write report", 90), (row.Kind, row.ShortId, row.Title, row.Minutes));
        Assert.Equal(1, r.PeopleWithUnscheduledWork);
        Assert.DoesNotContain(typeof(LedTimeProgressResponse).GetProperties(), p => p.Name.Contains("Allocat"));
    }

    [Fact]
    public void Month_buckets_are_iso_weeks()
    {
        var ranges = LedTimeProgressCalculator.BucketRanges(LedPeriod.Month, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));

        Assert.Equal(
            new[]
            {
                (new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 4)),
                (new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 11)),
                (new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 18)),
                (new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 25)),
                (new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 31)),
            },
            ranges);
        Assert.Equal(7, LedTimeProgressCalculator.BucketRanges(LedPeriod.Week, Monday, Monday.AddDays(6)).Count);
    }

    [Fact]
    public void Coverage_null_without_plan()
    {
        var r = Week(Person("Ada", [], [new AttributionSession(TaskA, On(Today, 9), On(Today, 10))]));

        Assert.Null(r.CoveragePercent);
        Assert.Null(r.Previous.CoveragePercent);
    }

    [Fact]
    public void Coverage_is_scheduled_over_planned_time_so_far()
    {
        // Today 09-10 planned and worked; a block tomorrow is commitment, not yet a missed plan.
        var r = Week(Person("Ada",
            [new AttributionBlock(TaskA, On(Today, 9), On(Today, 10)), new AttributionBlock(TaskA, On(Today.AddDays(1), 9), On(Today.AddDays(1), 10))],
            [new AttributionSession(TaskA, On(Today, 9), On(Today, 10))]));

        Assert.Equal(120, r.PlannedMinutes);
        Assert.Equal(100m, r.CoveragePercent);
        Assert.Equal((60, 60, 0), (r.Buckets[2].PlannedMinutes, r.Buckets[2].ScheduledMinutes, r.Buckets[2].UnscheduledMinutes));
        Assert.Equal(60, r.Buckets[3].PlannedMinutes);
    }

    [Fact]
    public void Workload_ranks_by_planned_then_clocked_then_name()
    {
        AttributionBlock[] Days(int count) => Enumerable.Range(0, count)
            .Select(i => new AttributionBlock(TaskA, On(Monday.AddDays(i), 9), On(Monday.AddDays(i), 17))).ToArray();
        var heavy = Person("Zed", Days(3), []);                                     // 24 h planned
        var light = Person("Amy", Days(1), []);                                     // 8 h planned
        var lightButWorked = Person("Bea", Days(1), [new AttributionSession(TaskA, On(Monday, 9), On(Monday, 10))]);
        var idle = Person("Cat", [], []);

        var r = Week(light, idle, heavy, lightButWorked);

        Assert.Equal(new[] { ("Zed", 1, 1440), ("Bea", 2, 480), ("Amy", 3, 480), ("Cat", 4, 0) },
            r.Workload.Select(w => (w.DisplayName, w.Rank, w.PlannedMinutes)));
    }

    [Fact]
    public void Utilization_over_100_is_kept()
    {
        var r = Week(
            Person("Ada", [new AttributionBlock(TaskA, On(Monday, 8), On(Monday, 18))], [], expected: 480),
            Person("Bob", [], [], expected: null));

        Assert.Equal(125.0m, r.Workload.Single(w => w.DisplayName == "Ada").UtilizationPercent);
        Assert.Null(r.Workload.Single(w => w.DisplayName == "Bob").UtilizationPercent);
        Assert.Null(r.Workload.Single(w => w.DisplayName == "Bob").ExpectedMinutes);
    }

    [Fact]
    public void Planner_off_keeps_clocked_and_zeroes_the_plan()
    {
        var people = new[]
        {
            Person("Ada", [new AttributionBlock(TaskA, On(Today, 9), On(Today, 10))], [new AttributionSession(TaskA, On(Today, 9), On(Today, 11))]),
        };

        var r = LedTimeProgressCalculator.Build(LedPeriod.Week, Monday, Monday.AddDays(6), Today, Now, plannerEnabled: false, people,
            new Dictionary<Guid, TeamTaskRow>());

        Assert.False(r.PlannerEnabled);
        Assert.Equal((0, 120, 0, 0, 0), (r.PlannedMinutes, r.ClockedMinutes, r.ScheduledMinutes, r.OutsideSlotMinutes, r.NoPlanMinutes));
        Assert.Null(r.CoveragePercent);
        Assert.Empty(r.Unscheduled);
        Assert.Equal(0, r.PeopleWithUnscheduledWork);
        Assert.All(r.Buckets, b => Assert.Equal((0, 0, 0), (b.PlannedMinutes, b.ScheduledMinutes, b.UnscheduledMinutes)));
    }

    [Fact]
    public void Previous_period_counts_only_the_same_elapsed_span()
    {
        // Last week: Mon 12 worked 60, Wed 14 at 20:00 worked 60 (after "now" a week ago, 18:00) - only the first counts.
        var r = Week(Person("Ada", [],
        [
            new AttributionSession(TaskA, On(new DateOnly(2026, 10, 12), 9), On(new DateOnly(2026, 10, 12), 10)),
            new AttributionSession(TaskA, On(new DateOnly(2026, 10, 14), 20), On(new DateOnly(2026, 10, 14), 21)),
        ]));

        Assert.Equal(60, r.Previous.ClockedMinutes);
        Assert.Equal(0, r.ClockedMinutes);
    }

    [Fact]
    public void Expected_minutes_use_working_days_hours_and_leave()
    {
        var expected = LedTimeProgressCalculator.ExpectedMinutes(new HashSet<int> { 1, 2, 3, 4, 5 }, new TimeOnly(9, 0), new TimeOnly(17, 0),
            new HashSet<DateOnly> { Monday }, Monday, Monday.AddDays(6), new DateOnly(2020, 1, 1), null,
            new Dictionary<DateOnly, decimal?> { [Monday.AddDays(1)] = null, [Monday.AddDays(2)] = 2m });

        // Tue-Fri expected (Mon a holiday) = 4 x 480; Tue a full leave day, Wed 2 h of leave.
        Assert.Equal(4 * 480 - 480 - 120, expected);
        Assert.Null(LedTimeProgressCalculator.ExpectedMinutes(new HashSet<int> { 1 }, null, null, new HashSet<DateOnly>(), Monday,
            Monday.AddDays(6), new DateOnly(2020, 1, 1), null, new Dictionary<DateOnly, decimal?>()));
    }
}
