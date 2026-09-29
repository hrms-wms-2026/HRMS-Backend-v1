using ONEVO.Application.Features.Monitoring.Exceptions.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Helpers;

/// <summary>
/// The figures a nightly rule compared, worded for a reviewer. Built once by ExceptionDetectionJob
/// and stored on the case, so the evidence shows exactly what was flagged even if attendance is
/// corrected later; the evidence query only rebuilds them for cases older than this.
/// </summary>
public static class ExceptionPatternMeasures
{
    public static IReadOnlyList<PatternMeasureDto> SustainedLowActivity(IReadOnlyCollection<decimal> ruleDayScores)
    {
        var measures = new List<PatternMeasureDto>
        {
            new("Alert when the activity score stays below", ExceptionDetectionRules.SustainedLowActivityScoreThreshold, "score"),
            new("For this many days in a row", ExceptionDetectionRules.SustainedLowActivityConsecutiveDays, "days")
        };
        if (ruleDayScores.Count > 0)
            measures.Add(new("Average score over those days", Math.Round(ruleDayScores.Average(), 1), "score"));
        return measures;
    }

    /// <remarks>Worked time here is the desktop app's work sessions - the source the rule uses -
    /// which can differ from the attendance record's worked minutes shown per day.</remarks>
    public static IReadOnlyList<PatternMeasureDto> AttendanceIrregularity(int thisWeekWorkedMinutes, int usualWeekWorkedMinutes) =>
    [
        new("Worked in the week (desktop app sessions)", thisWeekWorkedMinutes, "minutes"),
        new("Usual week (average of the 4 before)", usualWeekWorkedMinutes, "minutes"),
        new("Alert below", Math.Round(usualWeekWorkedMinutes * ExceptionDetectionRules.AttendanceIrregularityRatio), "minutes")
    ];

    public static IReadOnlyList<PatternMeasureDto> UnusualActivityPattern(decimal? dayScore, decimal thirtyDayAverage)
    {
        var measures = new List<PatternMeasureDto>();
        if (dayScore is decimal score)
            measures.Add(new("Activity score that day", score, "score"));
        measures.Add(new("30-day average", Math.Round(thirtyDayAverage, 1), "score"));
        measures.Add(new("Alert when the difference is more than", ExceptionDetectionRules.UnusualActivityDeviationPoints, "points"));
        return measures;
    }
}
