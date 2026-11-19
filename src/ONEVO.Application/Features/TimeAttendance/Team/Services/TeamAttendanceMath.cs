namespace ONEVO.Application.Features.TimeAttendance.Team.Services;

public enum TeamAttendanceMode { Week, Month }

/// <summary>The selected period [From, To] and the comparison period [PrevFrom, PrevTo]. While the selected period
/// contains today, the previous one is clipped to the same number of elapsed days.</summary>
public sealed record TeamAttendanceRange(DateOnly From, DateOnly To, DateOnly PrevFrom, DateOnly PrevTo, bool InProgress);

/// <summary>The Team Attendance widget's mutually exclusive day buckets.</summary>
public static class TeamAttendanceBuckets
{
    public const string Present = "present";
    public const string Late = "late";
    public const string Absent = "absent";
    public const string Leave = "leave";
    public const string Pending = "pending";
    public const string Off = "off";
}

/// <summary>
/// Period and rate rules for the My Team "Team attendance" widget. Late counts as attended; leave and pending
/// are never in the rate's denominator; the rate is weighted over employee-days (not an average of daily rates).
/// </summary>
public static class TeamAttendanceMath
{
    public static bool TryParseMode(string? mode, out TeamAttendanceMode parsed)
    {
        switch (mode?.Trim().ToLowerInvariant())
        {
            case "week":
                parsed = TeamAttendanceMode.Week;
                return true;
            case "month":
                parsed = TeamAttendanceMode.Month;
                return true;
            default:
                parsed = default;
                return false;
        }
    }

    public static TeamAttendanceRange Resolve(TeamAttendanceMode mode, DateOnly anchor, DateOnly today)
    {
        DateOnly from, to, prevFrom, prevTo;
        if (mode == TeamAttendanceMode.Week)
        {
            // ISO week: Monday first.
            from = anchor.AddDays(-(((int)anchor.DayOfWeek + 6) % 7));
            to = from.AddDays(6);
            prevFrom = from.AddDays(-7);
            prevTo = from.AddDays(-1);
        }
        else
        {
            from = new DateOnly(anchor.Year, anchor.Month, 1);
            to = from.AddMonths(1).AddDays(-1);
            prevFrom = from.AddMonths(-1);
            prevTo = from.AddDays(-1);
        }

        var inProgress = today >= from && today <= to;
        if (inProgress)
        {
            var clipped = prevFrom.AddDays(today.DayNumber - from.DayNumber);
            if (clipped < prevTo) prevTo = clipped;
        }

        return new TeamAttendanceRange(from, to, prevFrom, prevTo, inProgress);
    }

    /// <summary>Maps an AttendancePeriodCalculator.Classify day status to a widget bucket. <paramref name="isLate"/>
    /// is whether the date is in the classification's Late list, so a late arrival without a clock-out still shows
    /// as Late. Leave the viewer may not see masks to Absent, as Team Status does.</summary>
    public static string Bucket(string classifyStatus, bool isLate, bool leaveAuthorized) => classifyStatus switch
    {
        "present" or "missing_clock_out" => isLate ? TeamAttendanceBuckets.Late : TeamAttendanceBuckets.Present,
        "late" => TeamAttendanceBuckets.Late,
        "absent" => TeamAttendanceBuckets.Absent,
        "leave" => leaveAuthorized ? TeamAttendanceBuckets.Leave : TeamAttendanceBuckets.Absent,
        _ => TeamAttendanceBuckets.Off,
    };

    /// <summary>attended / denominator as a percentage to one decimal, or null when nothing was expected.</summary>
    public static decimal? Rate(int attended, int denominator) =>
        denominator <= 0 ? null : Math.Round(attended * 100m / denominator, 1, MidpointRounding.AwayFromZero);
}
