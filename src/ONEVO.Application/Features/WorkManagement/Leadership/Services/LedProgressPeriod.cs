using ONEVO.Application.Features.WorkManagement.Leadership.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Services;

/// <summary>Team Progress period switch (TEAM_PROGRESS_WIDGET_CONTRACT.md, "Addendum: period switch").</summary>
public enum LedPeriod
{
    All,
    Week,
    Month,
}

/// <summary>Due-date windows for the Team Progress period switch. Pure: the caller supplies
/// "today" (the UTC date, the same assumption GetLedWorkProgressQueryHandler makes).
/// Week = the ISO week (Monday..Sunday) containing today; Month = today's calendar month.
/// Both bounds are inclusive.</summary>
public static class LedProgressPeriod
{
    /// <summary>Accepted by led-progress/employees and led-progress/tasks (blank = all).</summary>
    public static readonly IReadOnlyDictionary<string, LedPeriod> Periods =
        new Dictionary<string, LedPeriod>(StringComparer.OrdinalIgnoreCase)
        {
            ["all"] = LedPeriod.All,
            ["week"] = LedPeriod.Week,
            ["month"] = LedPeriod.Month,
        };

    /// <summary>Accepted by led-progress/period - "all" is served by led-progress itself.</summary>
    public static readonly IReadOnlyDictionary<string, LedPeriod> WindowedPeriods =
        new Dictionary<string, LedPeriod>(StringComparer.OrdinalIgnoreCase)
        {
            ["week"] = LedPeriod.Week,
            ["month"] = LedPeriod.Month,
        };

    public static bool IsWindowed(string? value)
        => !string.IsNullOrWhiteSpace(value) && WindowedPeriods.ContainsKey(value.Trim());

    public static LedPeriod Parse(string? value) => LedProgressQueryOptions.Parse(value, Periods, LedPeriod.All);

    public static string Wire(LedPeriod period) => period switch
    {
        LedPeriod.Week => "week",
        LedPeriod.Month => "month",
        _ => "all",
    };

    /// <summary>The inclusive due-date window, or null for <see cref="LedPeriod.All"/> (no filter).</summary>
    public static LedDueWindow? Window(LedPeriod period, DateOnly today) => period switch
    {
        LedPeriod.Week => Week(today),
        LedPeriod.Month => Month(today),
        _ => null,
    };

    public static LedDueWindow Week(DateOnly today)
    {
        // DayOfWeek: Sunday = 0 .. Saturday = 6; ISO weeks start on Monday.
        var daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
        var monday = today.AddDays(-daysSinceMonday);
        return new LedDueWindow(monday, monday.AddDays(6));
    }

    public static LedDueWindow Month(DateOnly today)
    {
        var first = new DateOnly(today.Year, today.Month, 1);
        return new LedDueWindow(first, first.AddMonths(1).AddDays(-1));
    }

    /// <summary>Today's UTC date, as GetLedWorkProgressQueryHandler computes it.</summary>
    public static DateOnly UtcToday(DateTimeOffset utcNow) => DateOnly.FromDateTime(utcNow.UtcDateTime);
}
