using FluentAssertions;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using ONEVO.Infrastructure.Services.Monitoring.Exceptions;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Exceptions;

public class ExceptionDetectionRulesTests
{
    private static ActivityDailySummary Day(DateOnly date, decimal score) => new()
    {
        Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), EmployeeId = Guid.NewGuid(), Date = date, ActivityScore = score
    };

    [Fact]
    public void SustainedLowActivity_ThreeConsecutiveDaysBelowThreshold_Triggers()
    {
        var today = new DateOnly(2026, 8, 18);
        var days = new List<ActivityDailySummary>
        {
            Day(today.AddDays(-2), 30), Day(today.AddDays(-1), 25), Day(today, 20)
        };

        ExceptionDetectionRules.IsSustainedLowActivity(days, today).Should().BeTrue();
    }

    [Fact]
    public void SustainedLowActivity_OneGoodDayBreaksTheStreak_DoesNotTrigger()
    {
        var today = new DateOnly(2026, 8, 18);
        var days = new List<ActivityDailySummary>
        {
            Day(today.AddDays(-2), 30), Day(today.AddDays(-1), 60), Day(today, 20)
        };

        ExceptionDetectionRules.IsSustainedLowActivity(days, today).Should().BeFalse();
    }

    [Fact]
    public void SustainedLowActivity_MissingDayBreaksTheStreak_DoesNotTrigger()
    {
        var today = new DateOnly(2026, 8, 18);
        var days = new List<ActivityDailySummary> { Day(today.AddDays(-2), 30), Day(today, 20) };

        ExceptionDetectionRules.IsSustainedLowActivity(days, today).Should().BeFalse();
    }

    [Fact]
    public void IsAttendanceIrregularity_ThisWeekUnderHalfOfBaseline_Triggers()
    {
        ExceptionDetectionRules.IsAttendanceIrregularity(thisWeekWorkedMinutes: 400, trailingFourWeekAvgWorkedMinutes: 1200)
            .Should().BeTrue();
    }

    [Fact]
    public void IsAttendanceIrregularity_NoBaselineYet_DoesNotTrigger()
    {
        ExceptionDetectionRules.IsAttendanceIrregularity(thisWeekWorkedMinutes: 0, trailingFourWeekAvgWorkedMinutes: 0)
            .Should().BeFalse();
    }

    [Fact]
    public void IsUnusualActivityPattern_LargeDeviationFromThirtyDayAverage_Triggers()
    {
        ExceptionDetectionRules.IsUnusualActivityPattern(todayScore: 10m, thirtyDayAverageScore: 70m).Should().BeTrue();
    }

    [Fact]
    public void IsUnusualActivityPattern_SmallDeviation_DoesNotTrigger()
    {
        ExceptionDetectionRules.IsUnusualActivityPattern(todayScore: 65m, thirtyDayAverageScore: 70m).Should().BeFalse();
    }
}
