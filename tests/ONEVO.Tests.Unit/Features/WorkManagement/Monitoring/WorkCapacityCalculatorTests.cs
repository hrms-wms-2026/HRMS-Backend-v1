using ONEVO.Application.Features.WorkManagement.Monitoring.Services;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Monitoring;

public class WorkCapacityCalculatorTests
{
    private static TimeOnly T(int hour, int minute = 0) => new(hour, minute);

    [Fact]
    public void DailyHours_StartToEnd() => Assert.Equal(8m, WorkCapacityCalculator.DailyHours(T(9), T(17), null));

    [Fact]
    public void DailyHours_SubtractsBreak() => Assert.Equal(8m, WorkCapacityCalculator.DailyHours(T(9), T(18), 60));

    [Fact]
    public void DailyHours_OvernightWindowWraps() => Assert.Equal(8m, WorkCapacityCalculator.DailyHours(T(22), T(6), 0));

    [Fact]
    public void DailyHours_HalfHours() => Assert.Equal(7.5m, WorkCapacityCalculator.DailyHours(T(9), T(17), 30));

    [Fact]
    public void DailyHours_UnsetWindow_FallsBackToEight() => Assert.Equal(8m, WorkCapacityCalculator.DailyHours(null, T(17), 0));

    [Fact]
    public void DailyHours_BreakLongerThanWindow_FallsBackToEight() => Assert.Equal(8m, WorkCapacityCalculator.DailyHours(T(9), T(9, 30), 60));

    [Fact]
    public void WorkingDays_TwoWorkWeeks_IsTen()
        => Assert.Equal(10, WorkCapacityCalculator.WorkingDays(new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 9), WorkCalendar.Default.WorkingDays));

    [Fact]
    public void WorkingDays_SameDay_IsOneOnAWorkday()
        => Assert.Equal(1, WorkCapacityCalculator.WorkingDays(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 5), WorkCalendar.Default.WorkingDays));

    [Fact]
    public void WorkingDays_EndBeforeStart_IsZero()
        => Assert.Equal(0, WorkCapacityCalculator.WorkingDays(new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 5), WorkCalendar.Default.WorkingDays));

    [Fact]
    public void ParseWorkingDays_IsoNumbers_IncludeSaturdayAndSunday()
    {
        var days = WorkCapacityCalculator.ParseWorkingDays("[1,2,3,4,5,6,7]");
        Assert.Contains(DayOfWeek.Saturday, days);
        Assert.Contains(DayOfWeek.Sunday, days);
        Assert.Equal(7, days.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("junk")]
    [InlineData("[]")]
    public void ParseWorkingDays_BadOrEmpty_IsMondayToFriday(string? json)
        => Assert.Equal(WorkCalendar.Default.WorkingDays.OrderBy(d => d), WorkCapacityCalculator.ParseWorkingDays(json).OrderBy(d => d));

    [Fact]
    public void FromLegalEntity_UsesItsWindowAndDays()
    {
        var calendar = WorkCapacityCalculator.FromLegalEntity(new LegalEntity
        {
            WorkStartTime = T(8), WorkEndTime = T(14), BreakDurationMinutes = 0, StandardWorkingDays = "[1,2,3,4,5,6]"
        });

        Assert.Equal(6m, calendar.DailyHours);
        Assert.Contains(DayOfWeek.Saturday, calendar.WorkingDays);
    }

    [Fact]
    public void FromLegalEntity_Null_IsDefault()
    {
        var calendar = WorkCapacityCalculator.FromLegalEntity(null);
        Assert.Equal(8m, calendar.DailyHours);
        Assert.Equal(5, calendar.WorkingDays.Count);
    }

    [Fact]
    public void Capacity_IsPeopleTimesWorkingDaysTimesDailyHours()
        // 10 working days x 5 members x 8h = 400h (the module example from the spec).
        => Assert.Equal(400m, WorkCalendar.Default.Capacity(5, new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 9)));
}
