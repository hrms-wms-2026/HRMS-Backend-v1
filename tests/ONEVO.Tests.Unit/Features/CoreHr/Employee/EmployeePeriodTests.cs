using ONEVO.Application.Features.CoreHr.Employee.Helpers;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class EmployeePeriodTests
{
    [Fact]
    public void Resolve_DefaultsToTheCurrentCalendarMonth_WhenBothOmitted()
    {
        var result = EmployeePeriod.Resolve(null, null, new DateOnly(2026, 9, 30));

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2026, 9, 1), result.Value!.From);
        Assert.Equal(new DateOnly(2026, 9, 30), result.Value.To);
    }

    [Fact]
    public void Resolve_DefaultUsesTheRealLastDayOfShortMonths()
    {
        var result = EmployeePeriod.Resolve(null, null, new DateOnly(2026, 2, 10));

        Assert.Equal(new DateOnly(2026, 2, 28), result.Value!.To);
    }

    [Fact]
    public void Resolve_UsesTheGivenRange()
    {
        var result = EmployeePeriod.Resolve(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), new DateOnly(2026, 9, 30));

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2026, 8, 1), result.Value!.From);
        Assert.Equal(new DateOnly(2026, 8, 31), result.Value.To);
    }

    [Fact]
    public void Resolve_Fails400_WhenOnlyOneBoundIsGiven()
    {
        var onlyFrom = EmployeePeriod.Resolve(new DateOnly(2026, 8, 1), null, new DateOnly(2026, 9, 30));
        var onlyTo = EmployeePeriod.Resolve(null, new DateOnly(2026, 8, 31), new DateOnly(2026, 9, 30));

        Assert.False(onlyFrom.IsSuccess);
        Assert.Equal(400, onlyFrom.StatusCode);
        Assert.False(onlyTo.IsSuccess);
        Assert.Equal(400, onlyTo.StatusCode);
    }

    [Fact]
    public void Resolve_Fails400_WhenFromIsAfterTo()
    {
        var result = EmployeePeriod.Resolve(new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public void Resolve_AllowsExactly366Days_AndRejects367()
    {
        var ok = EmployeePeriod.Resolve(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31), new DateOnly(2026, 9, 30));
        var tooLong = EmployeePeriod.Resolve(new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 2), new DateOnly(2026, 9, 30));

        Assert.True(ok.IsSuccess);
        Assert.False(tooLong.IsSuccess);
        Assert.Equal(400, tooLong.StatusCode);
    }
}
