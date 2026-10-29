using FluentAssertions;
using ONEVO.Application.Features.WorkManagement.Leadership.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Leadership;

/// <summary>LedProgressPeriod: the due-date windows of the Team Progress period switch.</summary>
public sealed class LedProgressPeriodTests
{
    private static DateOnly D(string value) => DateOnly.Parse(value);

    [Theory]
    [InlineData("2026-10-19", "2026-10-19", "2026-10-25")] // Monday -> itself
    [InlineData("2026-10-21", "2026-10-19", "2026-10-25")] // Wednesday
    [InlineData("2026-10-25", "2026-10-19", "2026-10-25")] // Sunday -> the Monday before, not the next one
    [InlineData("2026-10-26", "2026-10-26", "2026-11-01")] // next Monday, week crosses a month end
    [InlineData("2026-01-01", "2025-12-29", "2026-01-04")] // Thursday, week starts in the previous year
    [InlineData("2027-01-03", "2026-12-28", "2027-01-03")] // Sunday, week started in the previous year
    [InlineData("2028-02-29", "2028-02-28", "2028-03-05")] // leap day
    public void Week_is_the_iso_week_monday_to_sunday(string today, string from, string to)
    {
        var window = LedProgressPeriod.Week(D(today));

        window.Should().Be(new LedDueWindow(D(from), D(to)));
        window.From.DayOfWeek.Should().Be(DayOfWeek.Monday);
        window.To.DayOfWeek.Should().Be(DayOfWeek.Sunday);
    }

    [Theory]
    [InlineData("2026-10-01", "2026-10-01", "2026-10-31")] // first day
    [InlineData("2026-10-31", "2026-10-01", "2026-10-31")] // last day
    [InlineData("2026-04-15", "2026-04-01", "2026-04-30")] // 30-day month
    [InlineData("2026-02-28", "2026-02-01", "2026-02-28")] // non-leap February
    [InlineData("2028-02-10", "2028-02-01", "2028-02-29")] // leap February
    [InlineData("2000-02-15", "2000-02-01", "2000-02-29")] // divisible by 400 -> leap
    [InlineData("2100-02-15", "2100-02-01", "2100-02-28")] // century -> not leap
    [InlineData("2026-12-31", "2026-12-01", "2026-12-31")] // year end
    [InlineData("2027-01-01", "2027-01-01", "2027-01-31")] // year start
    public void Month_is_the_calendar_month(string today, string from, string to)
        => LedProgressPeriod.Month(D(today)).Should().Be(new LedDueWindow(D(from), D(to)));

    [Fact]
    public void Window_dispatches_on_the_period_and_all_means_no_window()
    {
        var today = D("2026-10-21");

        LedProgressPeriod.Window(LedPeriod.All, today).Should().BeNull();
        LedProgressPeriod.Window(LedPeriod.Week, today).Should().Be(LedProgressPeriod.Week(today));
        LedProgressPeriod.Window(LedPeriod.Month, today).Should().Be(LedProgressPeriod.Month(today));
    }

    [Fact]
    public void Utc_today_ignores_the_offset()
    {
        // 23:30 at -05:00 on the 25th is already the 26th in UTC.
        LedProgressPeriod.UtcToday(new DateTimeOffset(2026, 10, 25, 23, 30, 0, TimeSpan.FromHours(-5)))
            .Should().Be(D("2026-10-26"));
    }

    [Theory]
    [InlineData(null, LedPeriod.All)]
    [InlineData("", LedPeriod.All)]
    [InlineData("all", LedPeriod.All)]
    [InlineData("WEEK", LedPeriod.Week)]
    [InlineData(" Month ", LedPeriod.Month)]
    public void Parse_is_case_insensitive_and_defaults_to_all(string? value, LedPeriod expected)
        => LedProgressPeriod.Parse(value).Should().Be(expected);

    [Theory]
    [InlineData("week", true)]
    [InlineData("Month", true)]
    [InlineData("all", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("year", false)]
    public void Only_week_and_month_are_windowed(string? value, bool expected)
        => LedProgressPeriod.IsWindowed(value).Should().Be(expected);

    [Theory]
    [InlineData(LedPeriod.All, "all")]
    [InlineData(LedPeriod.Week, "week")]
    [InlineData(LedPeriod.Month, "month")]
    public void Wire_values(LedPeriod period, string wire) => LedProgressPeriod.Wire(period).Should().Be(wire);
}
