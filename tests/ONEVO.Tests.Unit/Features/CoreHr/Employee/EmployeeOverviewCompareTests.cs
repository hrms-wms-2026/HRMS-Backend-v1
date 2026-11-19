using ONEVO.Application.Features.CoreHr.Employee.Helpers;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class EmployeeOverviewCompareTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("none")]
    [InlineData("NONE")]
    public void Parse_TreatsOmittedAndNoneAsOff(string? value)
    {
        var result = EmployeeOverviewCompare.Parse(value);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Theory]
    [InlineData("previous")]
    [InlineData("Previous")]
    public void Parse_TreatsPreviousAsOn(string value)
    {
        var result = EmployeeOverviewCompare.Parse(value);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void Parse_RejectsAnythingElseWith400()
    {
        var result = EmployeeOverviewCompare.Parse("last-year");

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }
}
