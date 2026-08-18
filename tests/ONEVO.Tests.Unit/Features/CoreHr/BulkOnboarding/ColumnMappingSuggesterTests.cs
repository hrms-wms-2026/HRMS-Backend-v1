using ONEVO.Application.Features.CoreHr.BulkOnboarding.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.BulkOnboarding;

public class ColumnMappingSuggesterTests
{
    [Fact]
    public void Suggest_ExactHeaderNames_MapsDirectly()
    {
        var headers = new[] { "First Name", "Last Name", "Work Email", "Start Date" };

        var mapping = ColumnMappingSuggester.Suggest(headers);

        Assert.Equal("First Name", mapping["firstName"]);
        Assert.Equal("Last Name", mapping["lastName"]);
        Assert.Equal("Work Email", mapping["workEmail"]);
        Assert.Equal("Start Date", mapping["startDate"]);
    }

    [Fact]
    public void Suggest_CaseInsensitiveAndAbbreviatedHeaders_StillMatches()
    {
        var headers = new[] { "email", "FIRSTNAME", "dept" };

        var mapping = ColumnMappingSuggester.Suggest(headers);

        Assert.Equal("email", mapping["workEmail"]);
        Assert.Equal("FIRSTNAME", mapping["firstName"]);
        Assert.Equal("dept", mapping["department"]);
    }

    [Fact]
    public void Suggest_NoMatchingHeader_ReturnsNullForThatField()
    {
        var headers = new[] { "Random Column" };

        var mapping = ColumnMappingSuggester.Suggest(headers);

        Assert.Null(mapping["employeeNumber"]);
    }
}
