using ONEVO.Application.Features.WorkManagement.Leadership.DTOs;
using ONEVO.Application.Features.WorkManagement.Leadership.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Services;

/// <summary>Week windows of the Team Progress weekly-completion chart (TEAM_PROGRESS_WIDGET_CONTRACT.md,
/// "Addendum 2"). Pure: the caller supplies "today" (the UTC date, as LedProgressPeriod.UtcToday).
/// Weeks are ISO weeks (Monday..Sunday, both inclusive), oldest first; the last one is the week
/// containing today (partial).</summary>
public static class LedCompletionWeeks
{
    public const int DefaultWeeks = 10;
    public const int MinWeeks = 1;
    public const int MaxWeeks = 26;

    public static IReadOnlyList<LedDueWindow> Windows(DateOnly today, int count)
    {
        count = Math.Clamp(count, MinWeeks, MaxWeeks);
        var currentMonday = LedProgressPeriod.Week(today).From;
        var weeks = new List<LedDueWindow>(count);
        for (var back = count - 1; back >= 0; back--)
        {
            var monday = currentMonday.AddDays(-7 * back);
            weeks.Add(new LedDueWindow(monday, monday.AddDays(6)));
        }
        return weeks;
    }

    /// <summary>The whole range the weeks cover: first Monday .. current Sunday.</summary>
    public static LedDueWindow Span(IReadOnlyList<LedDueWindow> weeks) => new(weeks[0].From, weeks[^1].To);

    /// <summary>Sums per-day completions into the weeks. Every week is present (zero-filled); days
    /// outside the span are ignored.</summary>
    public static IReadOnlyList<LedWeeklyCompletionWeek> Bucket(
        IReadOnlyList<LedDueWindow> weeks, IEnumerable<LedCompletionDayRow> days)
    {
        var hours = new decimal[weeks.Count];
        var counts = new int[weeks.Count];
        if (weeks.Count > 0)
        {
            var first = weeks[0].From;
            foreach (var day in days)
            {
                if (day.Day < first || day.Day > weeks[^1].To)
                    continue;
                var index = (day.Day.DayNumber - first.DayNumber) / 7;
                hours[index] += day.Hours;
                counts[index] += day.TaskCount;
            }
        }

        return weeks.Select((week, i) => new LedWeeklyCompletionWeek(week.From, week.To, hours[i], counts[i])).ToList();
    }
}
