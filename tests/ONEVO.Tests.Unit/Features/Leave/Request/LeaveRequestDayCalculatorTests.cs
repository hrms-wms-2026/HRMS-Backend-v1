using FluentAssertions;
using ONEVO.Application.Features.Leave.Request.Helpers;
using ONEVO.Domain.Features.Leave.Common;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Request;

public class LeaveRequestDayCalculatorTests
{
    [Fact]
    public void Calculate_UsesConfiguredWorkingDaysInsteadOfFixedWeekdays()
    {
        var result = new LeaveRequestDayCalculator().Calculate(new LeaveRequestDayCalculationInput(
            StartDate: new DateOnly(2026, 8, 17),
            EndDate: new DateOnly(2026, 8, 23),
            HalfDayPeriod: null,
            StandardWorkingDays: [2, 4],
            HolidayDates: []));

        result.TotalDays.Should().Be(2m);
        result.CountedDates.Should().Equal(
            new DateOnly(2026, 8, 18),
            new DateOnly(2026, 8, 20));
    }

    [Fact]
    public void Calculate_ExcludesConfiguredHolidayDates()
    {
        var result = new LeaveRequestDayCalculator().Calculate(new LeaveRequestDayCalculationInput(
            StartDate: new DateOnly(2026, 8, 17),
            EndDate: new DateOnly(2026, 8, 21),
            HalfDayPeriod: null,
            StandardWorkingDays: [1, 2, 3, 4, 5],
            HolidayDates: [new DateOnly(2026, 8, 19)]));

        result.TotalDays.Should().Be(4m);
        result.CountedDates.Should().NotContain(new DateOnly(2026, 8, 19));
    }

    [Theory]
    [InlineData(LeaveHalfDayPeriods.Am)]
    [InlineData(LeaveHalfDayPeriods.Pm)]
    public void Calculate_SingleDayHalfDay_ReturnsHalfDay(string halfDayPeriod)
    {
        var result = new LeaveRequestDayCalculator().Calculate(new LeaveRequestDayCalculationInput(
            StartDate: new DateOnly(2026, 8, 18),
            EndDate: new DateOnly(2026, 8, 18),
            HalfDayPeriod: halfDayPeriod,
            StandardWorkingDays: [1, 2, 3, 4, 5],
            HolidayDates: []));

        result.TotalDays.Should().Be(0.5m);
    }

    [Fact]
    public void Calculate_NonWorkingRange_ReturnsZero()
    {
        var result = new LeaveRequestDayCalculator().Calculate(new LeaveRequestDayCalculationInput(
            StartDate: new DateOnly(2026, 8, 22),
            EndDate: new DateOnly(2026, 8, 23),
            HalfDayPeriod: null,
            StandardWorkingDays: [1, 2, 3, 4, 5],
            HolidayDates: []));

        result.TotalDays.Should().Be(0m);
        result.CountedDates.Should().BeEmpty();
    }
}
