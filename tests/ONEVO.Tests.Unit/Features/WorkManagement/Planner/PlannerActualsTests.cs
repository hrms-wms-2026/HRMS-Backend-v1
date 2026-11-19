using ONEVO.Application.Features.WorkManagement.Planner.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Planner;

/// <summary>Day planner (2026-10-26 design, Part B): actual minutes for a day.</summary>
public class PlannerActualsTests
{
    private static readonly DateTimeOffset DayStart = new(2026, 11, 2, 0, 0, 0, TimeSpan.FromHours(5.5));
    private static readonly DateTimeOffset DayEnd = DayStart.AddDays(1);

    [Fact]
    public void Closed_session_inside_the_day()
    {
        var sessions = new[] { (DayStart.AddHours(9), (DateTimeOffset?)DayStart.AddHours(10).AddMinutes(30)) };

        Assert.Equal(90, PlannerActuals.TaskMinutes(sessions, DayStart, DayEnd, now: DayEnd.AddHours(1)));
    }

    [Fact]
    public void Open_session_from_yesterday_counts_today_only()
    {
        var sessions = new[] { (DayStart.AddHours(-2), (DateTimeOffset?)null) };

        Assert.Equal(180, PlannerActuals.TaskMinutes(sessions, DayStart, DayEnd, now: DayStart.AddHours(3)));
    }

    [Fact]
    public void Open_session_today_counts_until_now()
    {
        var sessions = new[] { (DayStart.AddHours(9), (DateTimeOffset?)null) };

        Assert.Equal(45, PlannerActuals.TaskMinutes(sessions, DayStart, DayEnd, now: DayStart.AddHours(9).AddMinutes(45)));
    }

    [Fact]
    public void Sessions_are_summed_and_a_session_outside_the_day_is_ignored()
    {
        var sessions = new[]
        {
            (DayStart.AddHours(9), (DateTimeOffset?)DayStart.AddHours(10)),
            (DayStart.AddHours(14), (DateTimeOffset?)DayStart.AddHours(14).AddMinutes(30)),
            (DayStart.AddDays(-1), (DateTimeOffset?)DayStart.AddDays(-1).AddHours(2)),
        };

        Assert.Equal(90, PlannerActuals.TaskMinutes(sessions, DayStart, DayEnd, now: DayEnd));
    }

    [Theory]
    [InlineData(true, false, true, 0)]
    [InlineData(false, true, true, 0)]
    [InlineData(false, false, false, 0)]
    [InlineData(false, false, true, 60)]
    public void Meeting_minutes(bool declined, bool cancelled, bool ended, int expected)
    {
        Assert.Equal(expected, PlannerActuals.MeetingMinutes(60, declined, cancelled, ended));
    }
}
