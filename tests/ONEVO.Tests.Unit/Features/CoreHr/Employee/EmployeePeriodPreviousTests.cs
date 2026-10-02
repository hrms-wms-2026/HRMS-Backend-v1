using ONEVO.Application.Features.CoreHr.Employee.Helpers;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class EmployeePeriodPreviousTests
{
    [Fact]
    public void Previous_OfAWholeMonth_IsThePreviousWholeMonth()
    {
        var previous = new EmployeePeriod(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)).Previous();

        Assert.Equal(new DateOnly(2026, 8, 1), previous.From);
        Assert.Equal(new DateOnly(2026, 8, 31), previous.To);
    }

    [Fact]
    public void Previous_CrossesTheYearBoundary_AndHandlesShortMonths()
    {
        var january = new EmployeePeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)).Previous();
        var march = new EmployeePeriod(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)).Previous();

        Assert.Equal(new DateOnly(2025, 12, 1), january.From);
        Assert.Equal(new DateOnly(2025, 12, 31), january.To);
        Assert.Equal(new DateOnly(2026, 2, 1), march.From);
        Assert.Equal(new DateOnly(2026, 2, 28), march.To);
    }

    [Fact]
    public void Previous_OfAnArbitraryRange_IsTheSameLengthEndingTheDayBefore()
    {
        var previous = new EmployeePeriod(new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 16)).Previous();

        Assert.Equal(new DateOnly(2026, 9, 3), previous.From);
        Assert.Equal(new DateOnly(2026, 9, 9), previous.To);
    }

    [Fact]
    public void Previous_OfAFirstToMidMonthRange_IsNotTreatedAsAWholeMonth()
    {
        var previous = new EmployeePeriod(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 15)).Previous();

        Assert.Equal(new DateOnly(2026, 8, 17), previous.From);
        Assert.Equal(new DateOnly(2026, 8, 31), previous.To);
    }
}
