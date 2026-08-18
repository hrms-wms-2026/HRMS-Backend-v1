using FluentAssertions;
using ONEVO.Infrastructure.Services.Monitoring.ActivityMonitoring;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.ActivityMonitoring;

public class AppCategoryClassifierTests
{
    [Theory]
    [InlineData("code.exe", AppCategory.Productive)]
    [InlineData("EXCEL.EXE", AppCategory.Productive)] // case-insensitive
    [InlineData("spotify.exe", AppCategory.Personal)]
    [InlineData("some_random_tool.exe", AppCategory.Unknown)]
    [InlineData(null, AppCategory.Unknown)]
    public void Classify_ReturnsExpectedCategory(string? processName, AppCategory expected)
    {
        AppCategoryClassifier.Classify(processName).Should().Be(expected);
    }
}
