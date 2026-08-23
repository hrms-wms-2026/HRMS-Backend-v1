using FluentAssertions;
using ONEVO.Application.Features.Leave.Calendar.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Calendar;

public class LeaveCalendarMonthRangeTests
{
    [Fact]
    public void From_BuildsInclusiveMonthRange()
    {
        var result = LeaveCalendarMonthRange.From(2026, 2);

        result.IsSuccess.Should().BeTrue();
        result.Value!.MonthStart.Should().Be(new DateOnly(2026, 2, 1));
        result.Value.MonthEnd.Should().Be(new DateOnly(2026, 2, 28));
        result.Value.Dates().Should().HaveCount(28);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void From_RejectsInvalidMonth(int month)
    {
        var result = LeaveCalendarMonthRange.From(2026, month);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Month must be between 1 and 12.");
    }
}
