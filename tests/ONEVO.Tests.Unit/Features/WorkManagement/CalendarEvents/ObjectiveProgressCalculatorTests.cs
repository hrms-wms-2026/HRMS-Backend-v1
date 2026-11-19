using ONEVO.Application.Features.WorkManagement.CalendarEvents.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.CalendarEvents;

public class ObjectiveProgressCalculatorTests
{
    [Fact]
    public void Calculate_WithNoTasks_ReturnsNull()
    {
        Assert.Null(ObjectiveProgressCalculator.Calculate(doneCount: 0, totalCount: 0));
    }

    [Theory]
    [InlineData(4, 10, 40)]
    [InlineData(10, 10, 100)]
    [InlineData(0, 3, 0)]
    [InlineData(1, 3, 33)] // rounds, doesn't truncate to 0
    [InlineData(2, 3, 67)]
    public void Calculate_RoundsToNearestPercent(int done, int total, int expected)
    {
        Assert.Equal(expected, ObjectiveProgressCalculator.Calculate(done, total));
    }
}
