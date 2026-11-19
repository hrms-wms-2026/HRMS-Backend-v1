using ONEVO.Application.Features.WorkManagement.Planner.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Planner;

/// <summary>My Team widgets (2026-10-29 design, section 5.2): scheduled, outside-slot and no-plan task time.</summary>
public class TaskTimeAttributionTests
{
    private static readonly DateTimeOffset DayStart = new(2026, 11, 2, 0, 0, 0, TimeSpan.FromHours(5.5));
    private static readonly DateTimeOffset DayEnd = DayStart.AddDays(1);
    private static readonly Guid A = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = new("00000000-0000-0000-0000-00000000000b");

    private static DateTimeOffset At(int hour, int minute = 0) => DayStart.AddHours(hour).AddMinutes(minute);

    private static AttributionResult Run(AttributionBlock[] blocks, AttributionSession[] sessions, DateTimeOffset? now = null)
    {
        var result = TaskTimeAttribution.Attribute(blocks, sessions, DayStart, DayEnd, now ?? DayEnd.AddHours(1));
        AssertInvariant(result);
        return result;
    }

    private static void AssertInvariant(AttributionResult r)
    {
        Assert.Equal(r.ClockedMinutes, r.ScheduledMinutes + r.OutsideSlotMinutes + r.NoPlanMinutes);
        foreach (var t in r.Tasks)
            Assert.Equal(t.ClockedMinutes, t.ScheduledMinutes + t.OutsideSlotMinutes + t.NoPlanMinutes);
    }

    [Fact]
    public void Brief_example_splits_scheduled_outside_and_no_plan()
    {
        var r = Run(
            [new AttributionBlock(A, At(9), At(10))],
            [new AttributionSession(A, At(9, 20), At(10, 20)), new AttributionSession(B, At(11), At(11, 45))]);

        Assert.Equal(60, r.PlannedMinutes);
        Assert.Equal(105, r.ClockedMinutes);
        Assert.Equal(40, r.ScheduledMinutes);
        Assert.Equal(20, r.OutsideSlotMinutes);
        Assert.Equal(45, r.NoPlanMinutes);
        Assert.Equal(0, r.ParallelClockMinutes);
        Assert.Collection(r.Unscheduled,
            s =>
            {
                Assert.Equal((A, At(10), At(10, 20), 20, UnscheduledKind.OutsideSlot), (s.TaskId, s.Start, s.End, s.Minutes, s.Kind));
            },
            s =>
            {
                Assert.Equal((B, At(11), At(11, 45), 45, UnscheduledKind.NoPlan), (s.TaskId, s.Start, s.End, s.Minutes, s.Kind));
            });
    }

    [Fact]
    public void Two_blocks_of_one_task_count_the_session_once()
    {
        var r = Run(
            [new AttributionBlock(A, At(9), At(10)), new AttributionBlock(A, At(11), At(12))],
            [new AttributionSession(A, At(9, 30), At(11, 30))]);

        Assert.Equal(120, r.PlannedMinutes);
        Assert.Equal(120, r.ClockedMinutes);
        Assert.Equal(60, r.ScheduledMinutes);
        Assert.Equal(60, r.OutsideSlotMinutes);
        Assert.Equal(0, r.NoPlanMinutes);
    }

    [Fact]
    public void Overlapping_sessions_of_one_task_are_merged()
    {
        var r = Run([], [new AttributionSession(A, At(9), At(10)), new AttributionSession(A, At(9, 30), At(10, 30))]);

        Assert.Equal(90, r.ClockedMinutes);
        Assert.Equal(90, r.NoPlanMinutes);
        Assert.Equal(0, r.ParallelClockMinutes);
        Assert.Single(r.Unscheduled);
    }

    [Fact]
    public void Parallel_sessions_on_two_tasks_are_kept_and_flagged()
    {
        var r = Run([], [new AttributionSession(A, At(9), At(10)), new AttributionSession(B, At(9, 30), At(10, 30))]);

        Assert.Equal(120, r.ClockedMinutes);
        Assert.Equal(30, r.ParallelClockMinutes);
    }

    [Fact]
    public void Open_session_counts_until_now()
    {
        var r = Run([], [new AttributionSession(A, At(9), null)], now: At(9, 45));

        Assert.Equal(45, r.ClockedMinutes);
    }

    [Fact]
    public void Session_crossing_midnight_is_clipped_to_the_window()
    {
        var r = Run([], [new AttributionSession(A, DayStart.AddHours(-1), At(1))]);

        Assert.Equal(60, r.ClockedMinutes);
        Assert.Equal(DayStart, Assert.Single(r.Unscheduled).Start);
    }

    [Fact]
    public void Blocks_outside_the_window_are_ignored()
    {
        var r = Run(
            [new AttributionBlock(A, DayStart.AddHours(-3), DayStart.AddHours(-1)), new AttributionBlock(A, DayEnd, DayEnd.AddHours(1))],
            [new AttributionSession(A, At(9), At(10))]);

        Assert.Equal(0, r.PlannedMinutes);
        Assert.Equal(60, r.NoPlanMinutes);
        Assert.Equal(0, r.OutsideSlotMinutes);
    }

    [Fact]
    public void Zero_or_negative_sessions_are_dropped()
    {
        var r = Run([], [new AttributionSession(A, At(9), At(9)), new AttributionSession(B, At(10), At(9))]);

        Assert.Equal(0, r.ClockedMinutes);
        Assert.Empty(r.Tasks);
        Assert.Empty(r.Unscheduled);
    }

    // ---- Coverage audit (2026-10-29 plan, Task 12) ----

    [Fact]
    public void Window_is_half_open_a_session_ending_at_from_or_starting_at_to_adds_nothing()
    {
        var r = Run([], [new AttributionSession(A, DayStart.AddHours(-2), DayStart), new AttributionSession(B, DayEnd, DayEnd.AddHours(1))],
            now: DayEnd.AddHours(2));

        Assert.Equal(0, r.ClockedMinutes);
        Assert.Empty(r.Unscheduled);
    }

    [Fact]
    public void A_block_touching_the_window_edge_counts_only_inside()
    {
        var r = Run([new AttributionBlock(A, DayStart.AddHours(-1), DayStart.AddHours(1))], [new AttributionSession(A, DayStart, DayStart.AddHours(1))]);

        Assert.Equal((60, 60, 60), (r.PlannedMinutes, r.ClockedMinutes, r.ScheduledMinutes));
    }

    [Fact]
    public void Now_inside_a_block_counts_the_open_session_up_to_now_only()
    {
        var r = Run([new AttributionBlock(A, At(9), At(11))], [new AttributionSession(A, At(9), null)], now: At(9, 30));

        Assert.Equal((120, 30, 30, 0), (r.PlannedMinutes, r.ClockedMinutes, r.ScheduledMinutes, r.OutsideSlotMinutes));
    }

    [Fact]
    public void A_block_of_task_A_while_clocked_on_task_B_is_no_plan_for_B_and_scheduled_zero_for_A()
    {
        var r = Run([new AttributionBlock(A, At(9), At(10))], [new AttributionSession(B, At(9), At(10))]);

        var a = r.Tasks.Single(t => t.TaskId == A);
        var b = r.Tasks.Single(t => t.TaskId == B);
        Assert.Equal((60, 0, 0), (a.PlannedMinutes, a.ClockedMinutes, a.ScheduledMinutes));
        Assert.Equal((0, 60, 0, 60), (b.PlannedMinutes, b.ClockedMinutes, b.ScheduledMinutes, b.NoPlanMinutes));
        Assert.Equal(0, r.ParallelClockMinutes);
    }

    [Fact]
    public void Sub_minute_pieces_round_away_from_zero_and_keep_the_invariant()
    {
        // 30 s inside the block, 30 s outside: the total rounds to 1 and the parts still add up to it.
        var r = Run([new AttributionBlock(A, At(9), At(10))], [new AttributionSession(A, At(9, 59).AddSeconds(30), At(10).AddSeconds(30))]);

        Assert.Equal(1, r.ClockedMinutes);
        Assert.Equal(r.ClockedMinutes, r.ScheduledMinutes + r.OutsideSlotMinutes);
    }
}
