using FluentAssertions;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Services;

namespace ONEVO.Tests.Unit.Features.Monitoring.ActivityMonitoring;

public class AppUsageCategorizerTests
{
    [Theory]
    [InlineData("Code.exe")]
    [InlineData("EXCEL.EXE")]
    [InlineData("postman")]
    public void Categorize_returns_productive_for_work_apps(string processName)
    {
        AppUsageCategorizer.Categorize(processName)
            .Should().Be(AppUsageCategory.Productive);
    }

    [Theory]
    [InlineData("Teams.exe")]
    [InlineData("zoom")]
    [InlineData("webex64")]
    public void Categorize_returns_meeting_for_meeting_apps(string processName)
    {
        AppUsageCategorizer.Categorize(processName)
            .Should().Be(AppUsageCategory.Meeting);
    }

    [Theory]
    [InlineData("youtube")]
    [InlineData("Spotify.exe")]
    [InlineData("steamwebhelper")]
    public void Categorize_returns_personal_for_personal_apps(string processName)
    {
        AppUsageCategorizer.Categorize(processName)
            .Should().Be(AppUsageCategory.Personal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("unknown-tool")]
    public void Categorize_returns_unknown_for_empty_or_unmatched_apps(string? processName)
    {
        AppUsageCategorizer.Categorize(processName)
            .Should().Be(AppUsageCategory.Unknown);
    }
}
