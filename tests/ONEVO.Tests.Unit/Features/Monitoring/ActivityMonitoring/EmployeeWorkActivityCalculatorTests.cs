using FluentAssertions;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Services;
using ONEVO.Application.Features.Monitoring.AppUsage.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.ActivityMonitoring;

public sealed class EmployeeWorkActivityCalculatorTests
{
    private static readonly TimeZoneInfo Colombo = TimeZoneInfo.CreateCustomTimeZone("c", TimeSpan.FromHours(5.5), "c", "c");
    private static readonly DateTimeOffset Day = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void HourlyActiveMinutes_MovesUtcHalfHourSlotsToLocalHours_AndReturns24Buckets()
    {
        var slots = new[]
        {
            new ActivitySlotRow(Day.AddHours(3).AddMinutes(30), 1800), // 03:30Z = 09:00 local
            new ActivitySlotRow(Day.AddHours(4), 600)                  // 04:00Z = 09:30 local
        };

        var hours = EmployeeWorkActivityCalculator.HourlyActiveMinutes(slots, Colombo);

        hours.Should().HaveCount(24);
        hours[9].ActiveMinutes.Should().Be(40);
        hours.Where(h => h.Hour != 9).Sum(h => h.ActiveMinutes).Should().Be(0);
    }

    [Fact]
    public void AppUsage_TakesTopByMinutes_AndCountsSessionsSplitByGapsOverTwoMinutes()
    {
        var totals = new[]
        {
            new AppProcessMinutesRow("code.exe", 5, Day.AddMinutes(20)),
            new AppProcessMinutesRow("chrome.exe", 2, Day.AddMinutes(8)),
            new AppProcessMinutesRow("slack.exe", 1, Day.AddMinutes(30))
        };
        var samples = new[]
        {
            new AppProcessSampleRow("code.exe", Day.AddMinutes(1)), new AppProcessSampleRow("code.exe", Day.AddMinutes(2)),
            new AppProcessSampleRow("code.exe", Day.AddMinutes(3)),                 // session 1
            new AppProcessSampleRow("code.exe", Day.AddMinutes(19)), new AppProcessSampleRow("code.exe", Day.AddMinutes(20)), // session 2
            new AppProcessSampleRow("chrome.exe", Day.AddMinutes(7)), new AppProcessSampleRow("chrome.exe", Day.AddMinutes(8))
        };

        var apps = EmployeeWorkActivityCalculator.AppUsage(totals, samples, take: 2);

        apps.Select(a => a.AppName).Should().Equal("code.exe", "chrome.exe");
        apps[0].ActiveMinutes.Should().Be(5);
        apps[0].Sessions.Should().Be(2);
        apps[0].LastUsedAt.Should().Be(Day.AddMinutes(20));
        apps[1].Sessions.Should().Be(1);
    }

    [Fact]
    public void LongestIdle_FindsTheLongestContiguousIdleRun()
    {
        ActivitySnapshot W(int minute, int active, int idle) => new() { CapturedAt = Day.AddMinutes(minute), ActiveSeconds = active, IdleSeconds = idle };
        var windows = new[]
        {
            W(1, 60, 0), W(2, 0, 60), W(3, 0, 60),            // idle 01:01-01:03 (2 min)
            W(4, 60, 0),
            W(5, 0, 60), W(6, 0, 60), W(7, 0, 60), W(8, 0, 60) // idle 00:04-00:08 (4 min)
        };

        var idle = EmployeeWorkActivityCalculator.LongestIdle(windows);

        idle!.Start.Should().Be(Day.AddMinutes(4));
        idle.End.Should().Be(Day.AddMinutes(8));
        idle.Minutes.Should().Be(4);
    }

    [Fact]
    public void LongestIdle_OfNoIdleWindows_IsNull()
    {
        EmployeeWorkActivityCalculator.LongestIdle(Array.Empty<ActivitySnapshot>()).Should().BeNull();
    }

    [Fact]
    public void UtcWindow_CoversWholeUtcDays()
    {
        var (from, to) = EmployeeWorkActivityCalculator.UtcWindow(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        from.Should().Be(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
        to.Should().Be(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
    }
}
