using System.Text.Json;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using ONEVO.Domain.Features.Monitoring.AppUsage.Entities;
using ONEVO.Domain.Features.Monitoring.Meetings.Entities;

namespace ONEVO.Infrastructure.Services.Monitoring.ActivityMonitoring;

/// <summary>
/// Pure aggregation of activity snapshots into a daily summary.
/// Extracted for unit testing without hosting infrastructure.
/// </summary>
public static class ActivityDailySummaryAggregator
{
    /// <summary>Default expected work minutes for data-coverage (8h).</summary>
    public const int DefaultExpectedWorkMinutes = 480;

    /// <summary>Minimum contiguous active minutes to count as focus.</summary>
    public const int FocusThresholdMinutes = 30;

    /// <summary>Each AppUsageCollector sample represents this many minutes of foreground time (its fixed 60s sample interval).</summary>
    private const int AppUsageMinutesPerSample = 1;

    /// <summary>Each MeetingDetector sample represents this many minutes (its fixed 2-minute sample interval).</summary>
    private const int MeetingMinutesPerSample = 2;

    private const int TopAppsLimit = 5;

    public static ActivityDailySummary Aggregate(
        Guid tenantId,
        Guid employeeId,
        DateOnly date,
        IReadOnlyList<ActivitySnapshot> snapshots,
        DateTimeOffset now,
        int expectedWorkMinutes = DefaultExpectedWorkMinutes,
        IReadOnlyList<AppUsageSnapshot>? appUsageSnapshots = null,
        IReadOnlyList<MeetingSignal>? meetingSignals = null)
    {
        var ordered = snapshots.OrderBy(s => s.CapturedAt).ToList();

        var totalActiveSeconds = ordered.Sum(s => s.ActiveSeconds);
        var totalIdleSeconds = ordered.Sum(s => s.IdleSeconds);
        var keyboardTotal = ordered.Sum(s => s.KeyboardEventsCount);
        var mouseTotal = ordered.Sum(s => s.MouseEventsCount);

        var totalActiveMinutes = totalActiveSeconds / 60;
        var totalIdleMinutes = totalIdleSeconds / 60;
        var totalMinutes = totalActiveMinutes + totalIdleMinutes;

        var activePercentage = totalMinutes > 0
            ? Math.Round((decimal)totalActiveMinutes / totalMinutes * 100m, 2)
            : 0m;

        var activeWithIntensity = ordered.Where(s => s.ActiveSeconds > 0).ToList();
        var intensityAvg = activeWithIntensity.Count > 0
            ? Math.Round(activeWithIntensity.Average(s => s.IntensityScore), 2)
            : 0m;

        var coveredMinutes = ordered.Sum(s => s.ActiveSeconds + s.IdleSeconds) / 60;
        var dataCoverage = expectedWorkMinutes > 0
            ? Math.Min(100m, Math.Round((decimal)coveredMinutes / expectedWorkMinutes * 100m, 2))
            : 0m;

        var (focusMinutes, deepFocusSessions) = ComputeFocus(ordered);

        var activityScore = Math.Round(
            activePercentage * (intensityAvg / 100m) * (dataCoverage / 100m),
            2);

        var (productiveMinutes, personalMinutes, unknownMinutes, topAppsJson) =
            AggregateAppUsage(appUsageSnapshots ?? []);

        var totalMeetingMinutes = (meetingSignals ?? [])
            .Count(s => s.IsMeetingAppRunning) * MeetingMinutesPerSample;

        return new ActivityDailySummary
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            Date = date,
            TotalActiveMinutes = totalActiveMinutes,
            TotalIdleMinutes = totalIdleMinutes,
            TotalMeetingMinutes = totalMeetingMinutes,
            ActivePercentage = activePercentage,
            ProductiveAppMinutes = productiveMinutes,
            PersonalAppMinutes = personalMinutes,
            UnknownAppMinutes = unknownMinutes,
            FocusMinutes = focusMinutes,
            ActivityScore = activityScore,
            DataCoveragePercentage = dataCoverage,
            TopAppsJson = topAppsJson,
            IntensityAvg = intensityAvg,
            KeyboardTotal = keyboardTotal,
            MouseTotal = mouseTotal,
            DocumentTimeMinutes = 0,
            DeepFocusSessionsCount = deepFocusSessions,
            DataSource = "agent_windows",
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static (int Productive, int Personal, int Unknown, string TopAppsJson) AggregateAppUsage(
        IReadOnlyList<AppUsageSnapshot> appUsageSnapshots)
    {
        if (appUsageSnapshots.Count == 0)
            return (0, 0, 0, "[]");

        var byProcess = appUsageSnapshots
            .GroupBy(s => s.ProcessName ?? "(unknown)")
            .Select(g => new { Process = g.Key, Minutes = g.Count() * AppUsageMinutesPerSample })
            .OrderByDescending(x => x.Minutes)
            .ToList();

        int productive = 0, personal = 0, unknown = 0;
        foreach (var app in byProcess)
        {
            var category = AppCategoryClassifier.Classify(app.Process);
            switch (category)
            {
                case AppCategory.Productive: productive += app.Minutes; break;
                case AppCategory.Personal: personal += app.Minutes; break;
                default: unknown += app.Minutes; break;
            }
        }

        var topApps = byProcess.Take(TopAppsLimit)
            .Select(x => new { process = x.Process, minutes = x.Minutes });
        var topAppsJson = JsonSerializer.Serialize(topApps);

        return (productive, personal, unknown, topAppsJson);
    }

    /// <summary>
    /// Contiguous active windows ≥ 30 min in the same foreground process.
    /// Snapshots are treated as sequential intervals ordered by CapturedAt.
    /// </summary>
    public static (int FocusMinutes, int DeepFocusSessions) ComputeFocus(
        IReadOnlyList<ActivitySnapshot> ordered)
    {
        if (ordered.Count == 0)
            return (0, 0);

        int focusMinutes = 0;
        int sessions = 0;

        string? currentProcess = null;
        int streakActiveSeconds = 0;

        void FlushStreak()
        {
            var minutes = streakActiveSeconds / 60;
            if (minutes >= FocusThresholdMinutes)
            {
                focusMinutes += minutes;
                sessions++;
            }
            streakActiveSeconds = 0;
            currentProcess = null;
        }

        foreach (var snap in ordered)
        {
            var process = snap.ForegroundProcessName ?? string.Empty;
            var isActive = snap.ActiveSeconds > 0;

            if (!isActive)
            {
                FlushStreak();
                continue;
            }

            if (currentProcess is null)
            {
                currentProcess = process;
                streakActiveSeconds = snap.ActiveSeconds;
            }
            else if (string.Equals(currentProcess, process, StringComparison.OrdinalIgnoreCase))
            {
                streakActiveSeconds += snap.ActiveSeconds;
            }
            else
            {
                FlushStreak();
                currentProcess = process;
                streakActiveSeconds = snap.ActiveSeconds;
            }
        }

        FlushStreak();
        return (focusMinutes, sessions);
    }
}
