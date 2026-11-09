namespace ONEVO.Application.Features.WorkManagement.Monitoring.Services;

public static class ForecastStatuses
{
    public const string OnTrack = "on_track";
    /// <summary>Behind, but it fits at a rate no higher than the max task rate (the UI says "Overtime risk").</summary>
    public const string RaiseRate = "raise_rate";
    /// <summary>Needs more than the max task rate (or there is no capacity left): pushing harder won't finish it.</summary>
    public const string Overload = "overload";
    public const string NoHistory = "no_history";

    public static readonly IReadOnlyList<string> All = [OnTrack, RaiseRate, Overload, NoHistory];

    /// <summary>Higher = worse. no_history sits between on_track and raise_rate only for ordering; it is never
    /// chosen by severity (a null rate always reads no_history).</summary>
    public static int Severity(string status) => status switch
    {
        Overload => 3,
        RaiseRate => 2,
        NoHistory => 1,
        _ => 0,
    };
}

/// <summary>The forecast figures of one module, one employee window or one total. Rate is the real conversion
/// rate (null = no history); Predicted, RequiredRate and OvertimeHours use rateEff = Rate ?? 1. Overload = the work
/// needs more than the max task rate (or there is no capacity), whatever the history; Status says no_history first.</summary>
public sealed record ForecastFigures(
    decimal Remaining, decimal Capacity, decimal? Rate, decimal Predicted, decimal? RequiredRate, decimal OvertimeHours, string Status,
    bool Overload = false);

/// <summary>
/// The max task rate: the most of their working time a team can realistically turn into task hours (~90-95%). Needing
/// more is overload. Stored per tenant in wm_task_rate_settings; no row means <see cref="Default"/>, and stored values
/// are clamped to [<see cref="Min"/>, <see cref="Max"/>].
/// </summary>
public static class TaskRateLimit
{
    public const decimal Default = 0.90m;
    public const decimal Min = 0.50m;
    public const decimal Max = 1.00m;

    public static decimal Resolve(decimal? stored) => stored is { } v ? Math.Clamp(v, Min, Max) : Default;
}

/// <summary>The binding EDF window of one employee: the one with the highest required rate (ties: larger shortfall).
/// Every figure, RequiredRate included, comes from that one window; Status is the most severe window status.</summary>
public sealed record EmployeeWindowForecast(DateOnly Deadline, ForecastFigures Figures, decimal? RequiredRate, string Status);

/// <summary>
/// The overtime forecast maths (spec 2026-11-08 §2), shared by the monitor rules, the live form checks and the
/// forecast widgets so they can never disagree. Pure.
/// predicted = capacity x rateEff; requiredRate = remaining / capacity; overtimeHours = max(0, remaining - predicted):
/// the TASK hours that won't get done in normal hours at the current rate, so it never exceeds remaining.
/// </summary>
public static class OvertimeForecast
{
    /// <summary>Only the last 4 weeks count: early slow performance must not drag down today's prediction.</summary>
    public const int WindowDays = 28;

    /// <summary>Below an hour of attendance in the window there is no rate.</summary>
    public const int MinWorkedMinutes = 60;

    /// <summary>[today - 28, today - 1]; today is partial, so it is excluded.</summary>
    public static (DateOnly Start, DateOnly End) Window(DateOnly today) => (today.AddDays(-WindowDays), today.AddDays(-1));

    /// <summary>min(1, task / worked); null when worked is under <see cref="MinWorkedMinutes"/>.</summary>
    public static decimal? Rate(int taskMinutes, int workedMinutes)
        => workedMinutes < MinWorkedMinutes ? null : Math.Min(1m, Math.Max(0, taskMinutes) / (decimal)workedMinutes);

    public static decimal Effective(decimal? rate) => rate ?? 1m;

    public static ForecastFigures Compute(decimal remaining, decimal capacity, decimal? rate, decimal maxTaskRate = TaskRateLimit.Default)
    {
        remaining = Math.Max(0m, remaining);
        capacity = Math.Max(0m, capacity);
        var rateEff = Effective(rate);
        var predicted = capacity * rateEff;
        decimal? requiredRate = capacity > 0 ? remaining / capacity : null;
        var overtime = Math.Max(0m, remaining - predicted);
        var overload = remaining > 0 && (capacity == 0 || requiredRate > maxTaskRate);

        string status;
        if (rate is null)
            status = ForecastStatuses.NoHistory;
        else if (predicted >= remaining)
            status = ForecastStatuses.OnTrack;
        else if (overload)
            status = ForecastStatuses.Overload;
        else
            status = ForecastStatuses.RaiseRate;

        return new ForecastFigures(remaining, capacity, rate, predicted, requiredRate, overtime, status, overload);
    }

    /// <summary>EDF for one employee: for every distinct deadline d, the remaining hours due on or before d against
    /// capacity(1, today, d) x rateEff. Items due before today are ignored. Null when nothing is due.</summary>
    public static EmployeeWindowForecast? EmployeeWindows(
        IEnumerable<(DateOnly Due, decimal Remaining)> items, WorkCalendar calendar, DateOnly today, decimal? rate,
        decimal maxTaskRate = TaskRateLimit.Default)
    {
        var open = items.Where(x => x.Due >= today).OrderBy(x => x.Due).ToList();
        if (open.Count == 0)
            return null;

        var windows = open.Select(x => x.Due).Distinct()
            .Select(deadline => (Deadline: deadline, Figures: Compute(
                open.Where(x => x.Due <= deadline).Sum(x => x.Remaining), calendar.Capacity(1, today, deadline), rate, maxTaskRate)))
            .ToList();

        // No capacity left with work due reads as an unbounded required rate, so such a window always binds.
        static decimal RequiredOrder(ForecastFigures f) => f.RequiredRate ?? (f.Remaining > 0 ? decimal.MaxValue : 0m);
        var binding = windows
            .OrderByDescending(w => RequiredOrder(w.Figures))
            .ThenByDescending(w => w.Figures.OvertimeHours)
            .ThenBy(w => w.Deadline)
            .First();
        var status = rate is null
            ? ForecastStatuses.NoHistory
            : windows.Select(w => w.Figures.Status).OrderByDescending(ForecastStatuses.Severity).First();
        return new EmployeeWindowForecast(binding.Deadline, binding.Figures, binding.Figures.RequiredRate, status);
    }
}
