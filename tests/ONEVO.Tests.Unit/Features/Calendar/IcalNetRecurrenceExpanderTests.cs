using ONEVO.Infrastructure.Services.Calendar;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class IcalNetRecurrenceExpanderTests
{
    private readonly IcalNetRecurrenceExpander _sut = new();

    [Fact]
    public void Expand_Daily_ReturnsOneOccurrencePerDayInRange()
    {
        var seriesStart = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
        var from = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero);

        var result = _sut.Expand("FREQ=DAILY", seriesStart, from, to);

        Assert.Equal(4, result.Count); // Sep 1, 2, 3, 4 (to=Sep 5 00:00 is exclusive)
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero), result[0]);
        Assert.Equal(new DateTimeOffset(2026, 9, 4, 9, 0, 0, TimeSpan.Zero), result[3]);
    }

    [Fact]
    public void Expand_Weekly_DefaultsToSeriesStartWeekday()
    {
        var seriesStart = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero); // Tuesday
        var from = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);

        var result = _sut.Expand("FREQ=WEEKLY", seriesStart, from, to);

        Assert.Equal(5, result.Count); // Sep 1, 8, 15, 22, 29
        Assert.All(result, d => Assert.Equal(DayOfWeek.Tuesday, d.DayOfWeek));
    }

    [Fact]
    public void Expand_Monthly_DefaultsToSeriesStartDayOfMonth()
    {
        var seriesStart = new DateTimeOffset(2026, 9, 15, 9, 0, 0, TimeSpan.Zero);
        var from = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);

        var result = _sut.Expand("FREQ=MONTHLY", seriesStart, from, to);

        Assert.Equal(3, result.Count); // Sep 15, Oct 15, Nov 15
        Assert.All(result, d => Assert.Equal(15, d.Day));
    }

    [Fact]
    public void Expand_WithUntil_StopsAtUntilDate()
    {
        var seriesStart = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
        var from = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);

        var result = _sut.Expand("FREQ=DAILY;UNTIL=20260903T090000Z", seriesStart, from, to);

        Assert.Equal(3, result.Count); // Sep 1, 2, 3
    }

    [Fact]
    public void Expand_RangeStartsMidSeries_OnlyReturnsOccurrencesFromRangeStart()
    {
        var seriesStart = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero); // series began in August
        var from = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);

        var result = _sut.Expand("FREQ=DAILY", seriesStart, from, to);

        Assert.Equal(7, result.Count); // Sep 1-7
        Assert.All(result, d => Assert.True(d >= from));
    }
}
