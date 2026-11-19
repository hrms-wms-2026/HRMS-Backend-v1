namespace ONEVO.Application.Features.WorkManagement.Planner.Services;

public enum NotRankedReason { None, NoItemsYet, NoPlannedTaskTime, OnLeave, PlannerOff }

/// <summary>Percent values have one decimal; a part is null when its denominator is 0, and Score only exists when both do.</summary>
public sealed record ExecutionScoreResult(decimal? ItemCompletionPercent, decimal? TimeCoveragePercent, decimal? Score, NotRankedReason Reason);

/// <summary>"Execution score (today)" of Today's Team Schedule (My Team widgets design 2026-10-29, section 6.3):
/// 0.6 x item completion (completed eligible items / items due by now) + 0.4 x time coverage (scheduled-attributed task
/// minutes / planned task minutes elapsed by now, capped at 100). Unscheduled time is shown beside it, never subtracted.</summary>
public static class ExecutionScore
{
    public const decimal ItemWeight = 0.6m, TimeWeight = 0.4m;

    public static ExecutionScoreResult Compute(int dueItems, int completedDueItems, int elapsedPlannedTaskMinutes,
        int scheduledAttributedMinutes, bool onLeave, bool plannerEnabled)
    {
        if (!plannerEnabled)
            return new ExecutionScoreResult(null, null, null, NotRankedReason.PlannerOff);
        if (onLeave)
            return new ExecutionScoreResult(null, null, null, NotRankedReason.OnLeave);

        decimal? item = dueItems > 0 ? 100m * Math.Min(completedDueItems, dueItems) / dueItems : null;
        decimal? time = elapsedPlannedTaskMinutes > 0
            ? Math.Min(100m, 100m * Math.Max(0, scheduledAttributedMinutes) / elapsedPlannedTaskMinutes)
            : null;

        var reason = item is null ? NotRankedReason.NoItemsYet
            : time is null ? NotRankedReason.NoPlannedTaskTime
            : NotRankedReason.None;
        decimal? score = item is { } i && time is { } t ? Round(ItemWeight * i + TimeWeight * t) : null;
        return new ExecutionScoreResult(Round(item), Round(time), score, reason);
    }

    /// <summary>Scored rows first by Score desc, then item completion desc, time coverage desc, name; unscored rows after,
    /// by name.</summary>
    public static IReadOnlyList<T> Rank<T>(IEnumerable<T> rows, Func<T, ExecutionScoreResult> score, Func<T, string> name)
    {
        var list = rows.ToList();
        var scored = list.Where(r => score(r).Score is not null)
            .OrderByDescending(r => score(r).Score)
            .ThenByDescending(r => score(r).ItemCompletionPercent)
            .ThenByDescending(r => score(r).TimeCoveragePercent)
            .ThenBy(name, StringComparer.OrdinalIgnoreCase);
        var unscored = list.Where(r => score(r).Score is null).OrderBy(name, StringComparer.OrdinalIgnoreCase);
        return scored.Concat(unscored).ToList();
    }

    private static decimal? Round(decimal? value) => value is { } v ? Math.Round(v, 1, MidpointRounding.AwayFromZero) : null;
}
