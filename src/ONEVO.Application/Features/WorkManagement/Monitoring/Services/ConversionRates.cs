namespace ONEVO.Application.Features.WorkManagement.Monitoring.Services;

/// <summary>One employee's 4-week figures. WeekWorked / WeekTask hold the 4 seven-day buckets of the window,
/// oldest first (bucket i starts at WindowStart + 7i).</summary>
public sealed record EmployeeConversionRate(
    Guid EmployeeId, int WorkedMinutes, int TaskMinutes, decimal? Rate, IReadOnlyList<int> WeekWorked, IReadOnlyList<int> WeekTask);

/// <param name="Contributors">People in the pool with worked time in the window: the real size of the pool.</param>
public sealed record PooledRate(int WorkedMinutes, int TaskMinutes, decimal? Rate, int Contributors = 0);

public sealed record RateTrendPoint(DateOnly WeekStart, decimal? Rate);

/// <summary>Conversion rates of a set of employees over the forecast window. A group's rate is pooled (sum of task
/// minutes / sum of worked minutes), never an average of individual rates.</summary>
public sealed record ConversionRateSet(
    DateOnly Today, DateOnly WindowStart, DateOnly WindowEnd, IReadOnlyDictionary<Guid, EmployeeConversionRate> ByEmployee)
{
    public const int Weeks = OvertimeForecast.WindowDays / 7;

    public static ConversionRateSet Empty(DateOnly today)
    {
        var (start, end) = OvertimeForecast.Window(today);
        return new ConversionRateSet(today, start, end, new Dictionary<Guid, EmployeeConversionRate>());
    }

    public decimal? RateOf(Guid employeeId) => ByEmployee.GetValueOrDefault(employeeId)?.Rate;

    public PooledRate Pool(IEnumerable<Guid> employeeIds)
    {
        int worked = 0, task = 0, contributors = 0;
        foreach (var id in employeeIds.Distinct())
        {
            if (!ByEmployee.TryGetValue(id, out var r))
                continue;
            worked += r.WorkedMinutes;
            task += r.TaskMinutes;
            if (r.WorkedMinutes > 0)
                contributors++;
        }
        return new PooledRate(worked, task, OvertimeForecast.Rate(task, worked), contributors);
    }

    public IReadOnlyList<RateTrendPoint> Trend(IEnumerable<Guid> employeeIds)
    {
        var people = employeeIds.Distinct().Select(id => ByEmployee.GetValueOrDefault(id)).OfType<EmployeeConversionRate>().ToList();
        return Enumerable.Range(0, Weeks)
            .Select(i => new RateTrendPoint(WindowStart.AddDays(7 * i), OvertimeForecast.Rate(
                people.Sum(p => p.WeekTask.ElementAtOrDefault(i)), people.Sum(p => p.WeekWorked.ElementAtOrDefault(i)))))
            .ToList();
    }
}
