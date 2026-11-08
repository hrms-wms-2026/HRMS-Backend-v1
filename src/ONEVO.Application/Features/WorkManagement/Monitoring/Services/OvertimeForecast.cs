namespace ONEVO.Application.Features.WorkManagement.Monitoring.Services;

public static class ForecastStatuses
{
    public const string OnTrack = "on_track";
    public const string RaiseRate = "raise_rate";
    public const string Overtime = "overtime";
    public const string NoHistory = "no_history";

    public static readonly IReadOnlyList<string> All = [OnTrack, RaiseRate, Overtime, NoHistory];

    /// <summary>Higher = worse. no_history sits between on_track and raise_rate only for ordering; it is never
    /// chosen by severity (a null rate always reads no_history).</summary>
    public static int Severity(string status) => status switch
    {
        Overtime => 3,
        RaiseRate => 2,
        NoHistory => 1,
        _ => 0,
    };
}

/// <summary>The forecast figures of one module, one employee window or one total. Rate is the real conversion
/// rate (null = no history); Predicted, RequiredRate and OvertimeHours use rateEff = Rate ?? 1.</summary>
public sealed record ForecastFigures(
    decimal Remaining, decimal Capacity, decimal? Rate, decimal Predicted, decimal? RequiredRate, decimal OvertimeHours, string Status);

/// <summary>The worst EDF window of one employee (largest overtime). RequiredRate is the max over all windows and
/// Status the most severe window status.</summary>
public sealed record EmployeeWindowForecast(DateOnly Deadline, ForecastFigures Figures, decimal? RequiredRate, string Status);

/// <summary>
/// The overtime forecast maths (spec 2026-11-08 §2), shared by the monitor rules, the live form checks and the
/// forecast widgets so they can never disagree. Pure.
/// predicted = capacity x rateEff; requiredRate = remaining / capacity; overtimeHours = max(0, remaining - predicted) / rateEff
/// (the extra WORK hours needed at the current rate; at rate 0 the shortfall itself, since no finite number of hours helps).
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

    public static ForecastFigures Compute(decimal remaining, decimal capacity, decimal? rate)
    {
        remaining = Math.Max(0m, remaining);
        capacity = Math.Max(0m, capacity);
        var rateEff = Effective(rate);
        var predicted = capacity * rateEff;
        decimal? requiredRate = capacity > 0 ? remaining / capacity : null;
        var shortfall = Math.Max(0m, remaining - predicted);
        var overtime = rateEff > 0 ? shortfall / rateEff : shortfall;

        string status;
        if (rate is null)
            status = ForecastStatuses.NoHistory;
        else if (predicted >= remaining)
            status = ForecastStatuses.OnTrack;
        else if (capacity == 0 || requiredRate > 1m)
            status = ForecastStatuses.Overtime;
        else
            status = ForecastStatuses.RaiseRate;

        return new ForecastFigures(remaining, capacity, rate, predicted, requiredRate, overtime, status);
    }

    /// <summary>EDF for one employee: for every distinct deadline d, the remaining hours due on or before d against
    /// capacity(1, today, d) x rateEff. Items due before today are ignored. Null when nothing is due.</summary>
    public static EmployeeWindowForecast? EmployeeWindows(
        IEnumerable<(DateOnly Due, decimal Remaining)> items, WorkCalendar calendar, DateOnly today, decimal? rate)
    {
        var open = items.Where(x => x.Due >= today).OrderBy(x => x.Due).ToList();
        if (open.Count == 0)
            return null;

        var windows = open.Select(x => x.Due).Distinct()
            .Select(deadline => (Deadline: deadline, Figures: Compute(
                open.Where(x => x.Due <= deadline).Sum(x => x.Remaining), calendar.Capacity(1, today, deadline), rate)))
            .ToList();

        var worst = windows
            .OrderByDescending(w => w.Figures.OvertimeHours)
            .ThenByDescending(w => w.Figures.RequiredRate ?? decimal.MaxValue)
            .ThenBy(w => w.Deadline)
            .First();
        var required = windows.Any(w => w.Figures.RequiredRate is null && w.Figures.Remaining > 0)
            ? null
            : windows.Max(w => w.Figures.RequiredRate);
        var status = rate is null
            ? ForecastStatuses.NoHistory
            : windows.Select(w => w.Figures.Status).OrderByDescending(ForecastStatuses.Severity).First();
        return new EmployeeWindowForecast(worst.Deadline, worst.Figures, required, status);
    }
}
