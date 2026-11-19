using FluentAssertions;
using ONEVO.Application.Features.TimeAttendance.Services;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class ExpectedWorkdayCalendarTests
{
    private static readonly IReadOnlySet<int> MonFri = new HashSet<int> { 1, 2, 3, 4, 5 };
    private static readonly IReadOnlySet<DateOnly> NoHolidays = new HashSet<DateOnly>();
    private static readonly DateOnly Sep1 = new(2026, 9, 1), Sep30 = new(2026, 9, 30);

    [Fact]
    public void FullMonth_CountsWeekdaysOnly()
    {
        var r = ExpectedWorkdayCalendar.Build(MonFri, NoHolidays, Sep1, Sep30, new DateOnly(2020, 1, 1), null, Sep30);
        r.Count.Should().Be(22); // Sep 2026: 22 weekdays (Tue 1st .. Wed 30th)
        r.ExpectedDates.Should().NotContain(new DateOnly(2026, 9, 5)); // Saturday
    }

    [Fact]
    public void Holidays_AreExcludedAndReported()
    {
        var holiday = new DateOnly(2026, 9, 14);
        var r = ExpectedWorkdayCalendar.Build(MonFri, new HashSet<DateOnly> { holiday }, Sep1, Sep30, new DateOnly(2020, 1, 1), null, Sep30);
        r.Count.Should().Be(21);
        r.HolidayDates.Should().Contain(holiday);
    }

    [Fact]
    public void MidMonthHire_ClipsTheStart()
    {
        var r = ExpectedWorkdayCalendar.Build(MonFri, NoHolidays, Sep1, Sep30, new DateOnly(2026, 9, 21), null, Sep30);
        r.EffectiveFrom.Should().Be(new DateOnly(2026, 9, 21));
        r.Count.Should().Be(8);
    }

    [Fact]
    public void Termination_ClipsTheEnd()
    {
        var r = ExpectedWorkdayCalendar.Build(MonFri, NoHolidays, Sep1, Sep30, new DateOnly(2020, 1, 1), new DateOnly(2026, 9, 4), Sep30);
        r.Count.Should().Be(4);
    }

    [Fact]
    public void Today_ClipsTheEnd_ButIncludesToday()
    {
        var r = ExpectedWorkdayCalendar.Build(MonFri, NoHolidays, Sep1, Sep30, new DateOnly(2020, 1, 1), null, new DateOnly(2026, 9, 2));
        r.Count.Should().Be(2);
    }

    [Fact]
    public void NotEmployedInPeriod_IsEmpty()
    {
        var r = ExpectedWorkdayCalendar.Build(MonFri, NoHolidays, Sep1, Sep30, new DateOnly(2026, 10, 5), null, Sep30);
        r.Count.Should().Be(0);
        r.EffectiveFrom.Should().BeNull();
        r.InEffectiveRange(new DateOnly(2026, 9, 10)).Should().BeFalse();
    }

    [Fact]
    public void NoWorkingWeekdays_MeansNoExpectedDays() // unconfigured schedule -> reader passes an empty set
    {
        var r = ExpectedWorkdayCalendar.Build(new HashSet<int>(), NoHolidays, Sep1, Sep30, new DateOnly(2020, 1, 1), null, Sep30);
        r.Count.Should().Be(0);
    }
}
