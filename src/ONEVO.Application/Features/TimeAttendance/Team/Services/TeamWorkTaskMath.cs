namespace ONEVO.Application.Features.TimeAttendance.Team.Services;

/// <summary>Pure period and ratio rules of the Work vs task hours widget, kept apart from the handler so they are unit tested.</summary>
public static class TeamWorkTaskMath
{
    public const string Day = "day";
    public const string Week = "week";
    public const string Month = "month";

    public static DateOnly MondayOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>The [from, to] range of the period containing <paramref name="anchor"/>.</summary>
    public static (DateOnly From, DateOnly To) RangeOf(string period, DateOnly anchor) => period switch
    {
        Day => (anchor, anchor),
        Week => (MondayOf(anchor), MondayOf(anchor).AddDays(6)),
        Month => (new DateOnly(anchor.Year, anchor.Month, 1),
            new DateOnly(anchor.Year, anchor.Month, DateTime.DaysInMonth(anchor.Year, anchor.Month))),
        _ => throw new ArgumentOutOfRangeException(nameof(period), period, "period must be day, week or month."),
    };

    /// <summary>The period right before the one starting at <paramref name="from"/>: the day, week or month before.</summary>
    public static (DateOnly From, DateOnly To) PreviousOf(string period, DateOnly from) => period switch
    {
        Day => (from.AddDays(-1), from.AddDays(-1)),
        Week => (from.AddDays(-7), from.AddDays(-1)),
        Month => RangeOf(Month, from.AddMonths(-1)),
        _ => throw new ArgumentOutOfRangeException(nameof(period), period, "period must be day, week or month."),
    };

    /// <summary>The days the chart shows: the ISO week around a day, otherwise the range itself.</summary>
    public static (DateOnly From, DateOnly To) TrendOf(string period, DateOnly from, DateOnly to) =>
        period == Day ? RangeOf(Week, from) : (from, to);

    /// <summary>Task time as a percent of working time, one decimal; null without working time.</summary>
    public static decimal? Utilization(int taskMinutes, int workedMinutes) =>
        workedMinutes <= 0 ? null : Math.Round(taskMinutes * 100m / workedMinutes, 1, MidpointRounding.AwayFromZero);

    /// <summary>An estimate in hours as whole minutes (null counts 0).</summary>
    public static int EstimateMinutes(decimal? hours) =>
        hours is { } h && h > 0 ? (int)Math.Round(h * 60m, MidpointRounding.AwayFromZero) : 0;

    /// <summary>Competition rank (1, 2, 2, 4) by utilization, highest first; null utilization is unranked.</summary>
    public static int? RankOf(decimal? value, IReadOnlyCollection<decimal> all) =>
        value is { } v ? all.Count(other => other > v) + 1 : null;
}
