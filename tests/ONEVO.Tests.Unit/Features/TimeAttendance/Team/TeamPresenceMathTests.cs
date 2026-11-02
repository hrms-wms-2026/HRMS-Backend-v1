using ONEVO.Application.Features.TimeAttendance.Team.Services;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.TimeAttendance.Team;

/// <summary>Working / Break / Leave (2026-10-29 design, section 6.4): since-when and time-left figures.</summary>
public sealed class TeamPresenceMathTests
{
    private static readonly DateTimeOffset ClockIn = new(2026, 9, 30, 9, 0, 0, TimeSpan.FromHours(5.5));

    private static AttendanceRecord Record() => new() { Id = Guid.NewGuid(), ActualStart = ClockIn };

    private static BreakRecord Break(int startMinute, int? endMinute) => new()
    {
        Id = Guid.NewGuid(), BreakStart = ClockIn.AddMinutes(startMinute),
        BreakEnd = endMinute is { } end ? ClockIn.AddMinutes(end) : null,
    };

    [Fact]
    public void State_since_is_clock_in_for_working()
        => Assert.Equal(ClockIn, TeamPresenceMath.StateSince("working", Record(), [Break(60, 75)]));

    [Fact]
    public void State_since_is_open_break_start_for_on_break()
        => Assert.Equal(ClockIn.AddMinutes(200), TeamPresenceMath.StateSince("on_break", Record(), [Break(60, 75), Break(200, null)]));

    [Theory]
    [InlineData("clocked_out")]
    [InlineData("on_leave")]
    [InlineData("absent")]
    public void State_since_is_null_for_other_statuses(string status)
        => Assert.Null(TeamPresenceMath.StateSince(status, Record(), [Break(200, null)]));

    [Fact]
    public void Break_remaining_is_null_without_configured_duration()
    {
        Assert.Null(TeamPresenceMath.BreakRemainingMinutes("on_break", null, 10));
        Assert.Null(TeamPresenceMath.BreakRemainingMinutes("on_break", 0, 10));
    }

    [Fact]
    public void Break_remaining_never_negative()
        => Assert.Equal(0, TeamPresenceMath.BreakRemainingMinutes("on_break", 30, 45));

    [Fact]
    public void Break_remaining_is_null_when_not_on_break()
        => Assert.Null(TeamPresenceMath.BreakRemainingMinutes("working", 30, 10));

    [Fact]
    public void Shift_end_null_for_on_leave()
    {
        var end = ClockIn.AddHours(8);
        Assert.Null(TeamPresenceMath.ShiftEndsAt("on_leave", end));
        Assert.Equal(end, TeamPresenceMath.ShiftEndsAt("working", end));
        Assert.Equal(end, TeamPresenceMath.ShiftEndsAt("on_break", end));
        Assert.Null(TeamPresenceMath.ShiftEndsAt("working", null));
    }

    [Fact]
    public void Scheduled_end_is_the_local_end_time_of_the_work_date()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo");

        Assert.Equal(new DateTimeOffset(2026, 9, 30, 17, 30, 0, TimeSpan.FromHours(5.5)),
            TeamPresenceMath.ScheduledEnd(new DateOnly(2026, 9, 30), new TimeOnly(17, 30), zone));
        Assert.Null(TeamPresenceMath.ScheduledEnd(new DateOnly(2026, 9, 30), null, zone));
    }

    // ---- Coverage audit (2026-10-29 plan, Task 12) ----

    [Theory]
    [InlineData(29, 1)]
    [InlineData(30, 0)]
    [InlineData(31, 0)]
    public void Break_remaining_around_the_allowance(int used, int expected)
        => Assert.Equal(expected, TeamPresenceMath.BreakRemainingMinutes("on_break", 30, used));

    [Fact]
    public void Clocked_out_has_no_since_no_remaining_and_no_shift_end()
    {
        Assert.Null(TeamPresenceMath.StateSince("clocked_out", Record(), []));
        Assert.Null(TeamPresenceMath.BreakRemainingMinutes("clocked_out", 30, 0));
        Assert.Null(TeamPresenceMath.ShiftEndsAt("clocked_out", ClockIn.AddHours(8)));
    }

    [Fact]
    public void On_break_without_an_open_break_has_no_since()
        => Assert.Null(TeamPresenceMath.StateSince("on_break", Record(), [Break(60, 75)]));
}
