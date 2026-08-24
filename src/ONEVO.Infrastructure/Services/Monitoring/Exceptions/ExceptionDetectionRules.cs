using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;

namespace ONEVO.Infrastructure.Services.Monitoring.Exceptions;

/// <summary>
/// Pure pattern-detection predicates. Extracted for unit testing without hosting
/// infrastructure, same pattern as ActivityDailySummaryAggregator/WellnessRuleEvaluator.
/// </summary>
public static class ExceptionDetectionRules
{
    public const int SustainedLowActivityConsecutiveDays = 3;
    public const decimal SustainedLowActivityScoreThreshold = 40m;
    public const decimal AttendanceIrregularityRatio = 0.5m;
    public const decimal UnusualActivityDeviationPoints = 40m;

    /// <summary>True if the most recent SustainedLowActivityConsecutiveDays calendar days each have
    /// a summary row with ActivityScore below threshold - a missing day or a day at/above threshold
    /// breaks the streak (no partial credit).</summary>
    public static bool IsSustainedLowActivity(IReadOnlyList<ActivityDailySummary> recentDaysAnyOrder, DateOnly today)
    {
        var byDate = recentDaysAnyOrder.ToDictionary(d => d.Date);

        for (var i = 0; i < SustainedLowActivityConsecutiveDays; i++)
        {
            var date = today.AddDays(-i);
            if (!byDate.TryGetValue(date, out var summary) || summary.ActivityScore >= SustainedLowActivityScoreThreshold)
                return false;
        }

        return true;
    }

    public static bool IsAttendanceIrregularity(int thisWeekWorkedMinutes, int trailingFourWeekAvgWorkedMinutes)
    {
        if (trailingFourWeekAvgWorkedMinutes <= 0)
            return false;

        return thisWeekWorkedMinutes < trailingFourWeekAvgWorkedMinutes * AttendanceIrregularityRatio;
    }

    public static bool IsUnusualActivityPattern(decimal todayScore, decimal thirtyDayAverageScore) =>
        Math.Abs(todayScore - thirtyDayAverageScore) > UnusualActivityDeviationPoints;
}
