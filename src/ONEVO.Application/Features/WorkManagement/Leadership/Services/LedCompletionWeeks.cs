using ONEVO.Application.Features.WorkManagement.Leadership.DTOs;
using ONEVO.Application.Features.WorkManagement.Leadership.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Services;

/// <summary>Week windows of the Team Progress weekly-completion chart (TEAM_PROGRESS_WIDGET_CONTRACT.md,
/// "Addendum 2"). Pure: the caller supplies "today" (the UTC date, as LedProgressPeriod.UtcToday).
/// Weeks are ISO weeks (Monday..Sunday, both inclusive), oldest first; the last one is the week
/// containing today (partial). With a period the buckets are those of led-progress/time
/// (<see cref="PeriodBuckets"/>).</summary>
public static class LedCompletionWeeks
{
    public const int DefaultWeeks = 10;
    public const int MinWeeks = 1;
    public const int MaxWeeks = 26;

    /// <summary>Wire values of LedWeeklyCompletionResponse.Granularity.</summary>
    public const string WeekGranularity = "week";
    public const string DayGranularity = "day";

    /// <summary>The buckets of a windowed period, the same as led-progress/time's
    /// (LedTimeProgressCalculator.BucketRanges): week = the 7 days Monday..Sunday of today's ISO
    /// week; month = the ISO weeks of today's month, clipped to it (first and last can be partial).</summary>
    public static IReadOnlyList<LedDueWindow> PeriodBuckets(LedPeriod period, DateOnly today)
    {
        var window = LedProgressPeriod.Window(period, today)
            ?? throw new ArgumentOutOfRangeException(nameof(period), period, "Period must be week or month.");
        return LedTimeProgressCalculator.BucketRanges(period, window.From, window.To)
            .Select(range => new LedDueWindow(range.From, range.To))
            .ToList();
    }

    /// <summary>"day" for the week period, "week" otherwise (month, or the last-N-weeks default).</summary>
    public static string Granularity(LedPeriod? period) => period == LedPeriod.Week ? DayGranularity : WeekGranularity;

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

    /// <summary>Sums per-day completions into the buckets (contiguous, oldest first: the weeks of
    /// <see cref="Windows"/> or <see cref="PeriodBuckets"/>). Every bucket is present (zero-filled);
    /// days outside the span are ignored.</summary>
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
                var index = IndexOf(weeks, day.Day);
                hours[index] += day.Hours;
                counts[index] += day.TaskCount;
            }
        }

        return weeks.Select((week, i) => new LedWeeklyCompletionWeek(week.From, week.To, hours[i], counts[i])).ToList();
    }

    // Buckets are contiguous and the day is inside the span, so a bucket always matches.
    private static int IndexOf(IReadOnlyList<LedDueWindow> buckets, DateOnly day)
    {
        int low = 0, high = buckets.Count - 1;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (day > buckets[mid].To) low = mid + 1;
            else high = mid;
        }
        return low;
    }
}
