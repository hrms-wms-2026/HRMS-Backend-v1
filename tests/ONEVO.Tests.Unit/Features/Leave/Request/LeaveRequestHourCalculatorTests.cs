using FluentAssertions;
using ONEVO.Application.Features.Leave.Request.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Request;

public class LeaveRequestHourCalculatorTests
{
    private static readonly TimeOnly Start = new(9, 0);
    private static readonly TimeOnly End = new(18, 0);
    private static readonly int[] Weekdays = [1, 2, 3, 4, 5];

    private static LeaveRequestHourCalculationResult Calc(
        DateTimeOffset from, DateTimeOffset to,
        TimeOnly? workStart = null, TimeOnly? workEnd = null, int breakMinutes = 60,
        IReadOnlyCollection<int>? days = null, IReadOnlyCollection<DateOnly>? holidays = null)
        => new LeaveRequestHourCalculator().Calculate(new LeaveRequestHourCalculationInput(
            from, to,
            workStart ?? Start, workEnd ?? End, breakMinutes,
            days ?? Weekdays, holidays ?? []));

    [Fact]
    public void FullDay_ChargesNetWorkDayHours()
    {
        var r = Calc(
            new DateTimeOffset(2026, 9, 9, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 9, 18, 0, 0, TimeSpan.Zero));
        r.TotalHours.Should().Be(8.00m);
        r.CountedShiftStartDates.Should().Equal(new DateOnly(2026, 9, 9));
    }

    [Fact]
    public void AfternoonPartial_DoesNotSubtractBreak()
    {
        var r = Calc(
            new DateTimeOffset(2026, 9, 9, 14, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 9, 18, 0, 0, TimeSpan.Zero));
        r.TotalHours.Should().Be(4.00m);
    }

    [Fact]
    public void OvernightNightShift_FullWindow()
    {
        var r = Calc(
            new DateTimeOffset(2026, 9, 9, 22, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 10, 6, 0, 0, TimeSpan.Zero),
            new TimeOnly(22, 0), new TimeOnly(6, 0), 0);
        r.TotalHours.Should().Be(8.00m);
        r.CountedShiftStartDates.Should().Equal(new DateOnly(2026, 9, 9));
    }

    [Fact]
    public void MultiDay_ThreeFullShifts()
    {
        var r = Calc(
            new DateTimeOffset(2026, 9, 9, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 11, 18, 0, 0, TimeSpan.Zero));
        r.TotalHours.Should().Be(24.00m);
    }

    [Fact]
    public void PartialEdges_FourPlusEightPlusFour()
    {
        var r = Calc(
            new DateTimeOffset(2026, 9, 9, 14, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 11, 13, 0, 0, TimeSpan.Zero));
        r.TotalHours.Should().Be(16.00m);
    }

    [Fact]
    public void WeekendSkip_FriToMon()
    {
        var r = Calc(
            new DateTimeOffset(2026, 9, 11, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 14, 18, 0, 0, TimeSpan.Zero));
        r.TotalHours.Should().Be(16.00m);
    }

    [Fact]
    public void EndAtNotAfterStartAt_Zero()
    {
        var at = new DateTimeOffset(2026, 9, 9, 9, 0, 0, TimeSpan.Zero);
        Calc(at, at).TotalHours.Should().Be(0m);
        Calc(at, at.AddMinutes(-1)).TotalHours.Should().Be(0m);
    }
}
