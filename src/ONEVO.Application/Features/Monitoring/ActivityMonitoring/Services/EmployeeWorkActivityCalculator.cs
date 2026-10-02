using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.AppUsage.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Services;

/// <summary>Pure shaping for the employee Work & Activity agent cards (Plan 4B).</summary>
public static class EmployeeWorkActivityCalculator
{
    public const int MaxRawDays = 31;
    public const int TopApps = 5;
    private static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan IdleContinuityTolerance = TimeSpan.FromSeconds(90);

    public static (DateTimeOffset From, DateTimeOffset To) UtcWindow(DateOnly from, DateOnly to) =>
        (new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
         new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

    public static bool ExceedsRawRange(DateOnly from, DateOnly to) => to.DayNumber - from.DayNumber + 1 > MaxRawDays;

    public static IReadOnlyList<ActivityHourBucket> HourlyActiveMinutes(IReadOnlyList<ActivitySlotRow> slots, TimeZoneInfo zone)
    {
        var seconds = new long[24];
        foreach (var slot in slots)
            seconds[TimeZoneInfo.ConvertTime(slot.SlotStartUtc, zone).Hour] += slot.ActiveSeconds;
        return Enumerable.Range(0, 24).Select(h => new ActivityHourBucket(h, (int)(seconds[h] / 60))).ToList();
    }

    public static IReadOnlyList<EmployeeAppUsageItem> AppUsage(
        IReadOnlyList<AppProcessMinutesRow> totals, IReadOnlyList<AppProcessSampleRow> samples, int take)
    {
        var sessions = samples
            .GroupBy(s => s.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => CountSessions(g.Select(s => s.CapturedAt)), StringComparer.OrdinalIgnoreCase);

        return totals
            .OrderByDescending(t => t.Samples)
            .ThenBy(t => t.ProcessName)
            .Take(take)
            .Select(t => new EmployeeAppUsageItem(
                t.ProcessName, t.Samples, sessions.TryGetValue(t.ProcessName, out var n) ? n : 0, t.LastCapturedAt))
            .ToList();
    }

    public static IReadOnlyList<string> TopProcessNames(IReadOnlyList<AppProcessMinutesRow> totals, int take) =>
        totals.OrderByDescending(t => t.Samples).ThenBy(t => t.ProcessName).Take(take).Select(t => t.ProcessName).ToList();

    public static EmployeeIdlePeriod? LongestIdle(IReadOnlyList<ActivitySnapshot> windows)
    {
        EmployeeIdlePeriod? best = null;
        DateTimeOffset? runStart = null, runEnd = null;

        foreach (var w in windows.OrderBy(w => w.CapturedAt))
        {
            var start = w.CapturedAt - TimeSpan.FromSeconds(w.ActiveSeconds + w.IdleSeconds);
            var isIdle = w.ActiveSeconds == 0 && w.IdleSeconds > 0;

            if (isIdle && runEnd is not null && start <= runEnd.Value + IdleContinuityTolerance)
            {
                runEnd = w.CapturedAt;
            }
            else if (isIdle)
            {
                runStart = start;
                runEnd = w.CapturedAt;
            }
            else
            {
                runStart = runEnd = null;
            }

            if (runStart is not null && runEnd is not null)
            {
                var minutes = (int)(runEnd.Value - runStart.Value).TotalMinutes;
                if (best is null || minutes > best.Minutes)
                    best = new EmployeeIdlePeriod(runStart.Value, runEnd.Value, minutes);
            }
        }
        return best;
    }

    private static int CountSessions(IEnumerable<DateTimeOffset> capturedAt)
    {
        var count = 0;
        DateTimeOffset? previous = null;
        foreach (var at in capturedAt.OrderBy(a => a))
        {
            if (previous is null || at - previous.Value > SessionGap)
                count++;
            previous = at;
        }
        return count;
    }
}
