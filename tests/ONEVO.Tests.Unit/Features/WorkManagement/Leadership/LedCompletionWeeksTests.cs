using FluentAssertions;
using ONEVO.Application.Features.WorkManagement.Leadership.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Leadership;

/// <summary>LedCompletionWeeks: the ISO week windows and bucketing of the weekly-completion chart.</summary>
public sealed class LedCompletionWeeksTests
{
    private static DateOnly D(string value) => DateOnly.Parse(value);

    [Fact]
    public void Ten_weeks_oldest_first_ending_with_the_current_week()
    {
        var weeks = LedCompletionWeeks.Windows(D("2026-10-21"), 10);

        weeks.Should().HaveCount(10);
        weeks[0].Should().Be(new LedDueWindow(D("2026-08-17"), D("2026-08-23")));
        weeks[^1].Should().Be(new LedDueWindow(D("2026-10-19"), D("2026-10-25")));
        weeks.Should().OnlyContain(w => w.From.DayOfWeek == DayOfWeek.Monday && w.To.DayNumber - w.From.DayNumber == 6);
        weeks.Zip(weeks.Skip(1)).Should().OnlyContain(pair => pair.Second.From == pair.First.To.AddDays(1), "contiguous, no gaps");
    }

    [Theory]
    [InlineData("2026-10-19", "2026-10-19")] // Monday -> its own week is current
    [InlineData("2026-10-25", "2026-10-19")] // Sunday -> still the week that started Monday
    [InlineData("2026-10-26", "2026-10-26")] // next Monday starts a new current week
    public void Current_week_follows_iso_monday_to_sunday(string today, string currentMonday)
        => LedCompletionWeeks.Windows(D(today), 3)[^1].From.Should().Be(D(currentMonday));

    [Fact]
    public void Weeks_cross_the_year_boundary()
    {
        // Thu 2027-01-07: current week Mon 4 Jan; the week of 28 Dec 2026 straddles the year end.
        var weeks = LedCompletionWeeks.Windows(D("2027-01-07"), 3);

        weeks.Should().Equal(
            new LedDueWindow(D("2026-12-21"), D("2026-12-27")),
            new LedDueWindow(D("2026-12-28"), D("2027-01-03")),
            new LedDueWindow(D("2027-01-04"), D("2027-01-10")));
        LedCompletionWeeks.Span(weeks).Should().Be(new LedDueWindow(D("2026-12-21"), D("2027-01-10")));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(26, 26)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(27, 26)]
    public void Count_is_clamped_to_1_to_26(int requested, int expected)
        => LedCompletionWeeks.Windows(D("2026-10-21"), requested).Should().HaveCount(expected);

    [Fact]
    public void Bucket_sums_days_into_weeks_zero_fills_and_ignores_days_outside_the_span()
    {
        var weeks = LedCompletionWeeks.Windows(D("2026-10-21"), 3); // 5 Oct .. 25 Oct

        var buckets = LedCompletionWeeks.Bucket(weeks,
        [
            new LedCompletionDayRow(D("2026-10-04"), 50, 5), // Sunday before the span
            new LedCompletionDayRow(D("2026-10-05"), 2, 1),  // first Monday
            new LedCompletionDayRow(D("2026-10-11"), 3, 2),  // first Sunday
            new LedCompletionDayRow(D("2026-10-19"), 1.5m, 1),
            new LedCompletionDayRow(D("2026-10-25"), 4, 1),  // current Sunday (future, still in the span)
            new LedCompletionDayRow(D("2026-10-26"), 70, 7), // after the span
        ]);

        buckets.Select(b => (b.WeekStart, b.WeekEnd, b.CompletedHours, b.CompletedTaskCount)).Should().Equal(
            (D("2026-10-05"), D("2026-10-11"), 5m, 3),
            (D("2026-10-12"), D("2026-10-18"), 0m, 0),
            (D("2026-10-19"), D("2026-10-25"), 5.5m, 2));
    }

    [Fact]
    public void Bucket_with_no_days_is_all_zero()
        => LedCompletionWeeks.Bucket(LedCompletionWeeks.Windows(D("2026-10-21"), 10), [])
            .Should().HaveCount(10).And.OnlyContain(b => b.CompletedHours == 0 && b.CompletedTaskCount == 0);
}
