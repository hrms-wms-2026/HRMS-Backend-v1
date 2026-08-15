using System.Text.Json;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Services;

/// <summary>
/// Pure aggregation of activity snapshots into a daily summary.
/// Extracted for unit testing without hosting infrastructure.
/// </summary>
public static class ActivityDailySummaryAggregator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>Default expected work minutes for data-coverage (8h).</summary>
    public const int DefaultExpectedWorkMinutes = 480;

    /// <summary>Minimum contiguous active minutes to count as focus.</summary>
    public const int FocusThresholdMinutes = 30;

    public static ActivityDailySummary Aggregate(
        Guid tenantId,
        Guid employeeId,
        DateOnly date,
        IReadOnlyList<ActivitySnapshot> snapshots,
        DateTimeOffset now,
        int expectedWorkMinutes = DefaultExpectedWorkMinutes)
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
        var appMetrics = ComputeAppMetrics(ordered);

        var activityScore = Math.Round(
            activePercentage * (intensityAvg / 100m) * (dataCoverage / 100m),
            2);

        return new ActivityDailySummary
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            Date = date,
            TotalActiveMinutes = totalActiveMinutes,
            TotalIdleMinutes = totalIdleMinutes,
            TotalMeetingMinutes = appMetrics.MeetingMinutes,
            ActivePercentage = activePercentage,
            ProductiveAppMinutes = appMetrics.ProductiveMinutes,
            PersonalAppMinutes = appMetrics.PersonalMinutes,
            UnknownAppMinutes = appMetrics.UnknownMinutes,
            FocusMinutes = focusMinutes,
            ActivityScore = activityScore,
            DataCoveragePercentage = dataCoverage,
            TopAppsJson = appMetrics.TopAppsJson,
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

    /// <summary>
    /// Contiguous active windows >= 30 min in the same foreground process.
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

    private static AppMetrics ComputeAppMetrics(IReadOnlyList<ActivitySnapshot> snapshots)
    {
        if (snapshots.Count == 0)
            return new AppMetrics(0, 0, 0, 0, "[]");

        var appGroups = snapshots
            .GroupBy(s => string.IsNullOrWhiteSpace(s.ForegroundProcessName)
                ? "unknown"
                : s.ForegroundProcessName.Trim())
            .Select(g =>
            {
                var totalSeconds = g.Sum(s => s.ActiveSeconds + s.IdleSeconds);
                var category = AppUsageCategorizer.Categorize(g.Key);
                return new
                {
                    AppName = g.Key,
                    TotalSeconds = totalSeconds,
                    Category = category,
                    FirstCapturedAt = g.Min(s => s.CapturedAt)
                };
            })
            .Where(g => g.TotalSeconds > 0)
            .ToList();

        var productiveMinutes = appGroups
            .Where(g => g.Category == AppUsageCategory.Productive)
            .Sum(g => g.TotalSeconds) / 60;

        var meetingMinutes = appGroups
            .Where(g => g.Category == AppUsageCategory.Meeting)
            .Sum(g => g.TotalSeconds) / 60;

        var personalMinutes = appGroups
            .Where(g => g.Category == AppUsageCategory.Personal)
            .Sum(g => g.TotalSeconds) / 60;

        var unknownMinutes = appGroups
            .Where(g => g.Category == AppUsageCategory.Unknown)
            .Sum(g => g.TotalSeconds) / 60;

        var topApps = appGroups
            .OrderByDescending(g => g.TotalSeconds)
            .ThenBy(g => g.FirstCapturedAt)
            .ThenBy(g => g.AppName, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .Select(g => new AppUsageSummary
            {
                AppName = g.AppName,
                TotalSeconds = g.TotalSeconds,
                Category = AppUsageCategorizer.ToWireValue(g.Category)
            })
            .ToList();

        var topAppsJson = topApps.Count == 0
            ? "[]"
            : JsonSerializer.Serialize(topApps, JsonOptions);

        return new AppMetrics(
            productiveMinutes,
            meetingMinutes,
            personalMinutes,
            unknownMinutes,
            topAppsJson);
    }

    private sealed record AppMetrics(
        int ProductiveMinutes,
        int MeetingMinutes,
        int PersonalMinutes,
        int UnknownMinutes,
        string TopAppsJson);
}
