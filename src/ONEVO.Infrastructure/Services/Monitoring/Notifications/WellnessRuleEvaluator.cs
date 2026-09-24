using ONEVO.Domain.Features.Monitoring.DeviceState.Entities;

namespace ONEVO.Infrastructure.Services.Monitoring.Notifications;

public sealed record WellnessEvaluationResult(bool BreakReminderTriggered, bool LongIdleTriggered, int StreakMinutes);

/// <summary>
/// Pure continuity analysis over DeviceStateSnapshot's fixed 60s sample interval — each
/// non-idle sample represents ~1 minute active, each idle sample ~1 minute idle. Extracted
/// for unit testing without hosting infrastructure, same pattern as ActivityDailySummaryAggregator.
/// </summary>
public static class WellnessRuleEvaluator
{
    public const int BreakReminderThresholdMinutes = 120;
    public const int LongIdleThresholdMinutes = 30;

    public static WellnessEvaluationResult Evaluate(IReadOnlyList<DeviceStateSnapshot> snapshotsOldestFirst, DateTimeOffset now)
    {
        if (snapshotsOldestFirst.Count == 0)
            return new WellnessEvaluationResult(false, false, 0);

        var latest = snapshotsOldestFirst[^1];
        var streakIsIdle = latest.IsIdle;
        var streakMinutes = 0;

        for (var i = snapshotsOldestFirst.Count - 1; i >= 0; i--)
        {
            if (snapshotsOldestFirst[i].IsIdle != streakIsIdle)
                break;
            streakMinutes++;
        }

        var breakTriggered = !streakIsIdle && streakMinutes >= BreakReminderThresholdMinutes;
        var idleTriggered = streakIsIdle && streakMinutes >= LongIdleThresholdMinutes;

        return new WellnessEvaluationResult(breakTriggered, idleTriggered, streakMinutes);
    }
}
