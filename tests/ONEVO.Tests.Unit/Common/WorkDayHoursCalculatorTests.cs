using FluentAssertions;
using ONEVO.Application.Common.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Common;

public class WorkDayHoursCalculatorTests
{
    [Fact]
    public void TryCompute_BothNull_ReturnsNull()
    {
        WorkDayHoursCalculator.TryCompute(null, null, 60).Should().BeNull();
    }

    [Fact]
    public void Compute_DayShiftMinusBreak()
    {
        WorkDayHoursCalculator.Compute(new TimeOnly(9, 0), new TimeOnly(18, 0), 60)
            .Should().Be(8.00m);
    }

    [Fact]
    public void Compute_OvernightNightShift()
    {
        WorkDayHoursCalculator.Compute(new TimeOnly(22, 0), new TimeOnly(6, 0), 0)
            .Should().Be(8.00m);
    }

    [Fact]
    public void Compute_EqualStartAndEnd_IsTwentyFourHours()
    {
        WorkDayHoursCalculator.Compute(new TimeOnly(9, 0), new TimeOnly(9, 0), 0)
            .Should().Be(24.00m);
    }

    [Fact]
    public void Compute_BreakLongerThanShift_ReturnsZeroOrNegativeDetected()
    {
        WorkDayHoursCalculator.Compute(new TimeOnly(9, 0), new TimeOnly(10, 0), 120)
            .Should().Be(-1.00m);
    }

    [Fact]
    public void ShiftInterval_Overnight_EndsNextDay()
    {
        var date = new DateOnly(2026, 9, 9);
        var (start, end) = WorkDayHoursCalculator.ShiftInterval(date, new TimeOnly(22, 0), new TimeOnly(6, 0));
        start.Should().Be(date.ToDateTime(new TimeOnly(22, 0)));
        end.Should().Be(date.AddDays(1).ToDateTime(new TimeOnly(6, 0)));
    }
}
