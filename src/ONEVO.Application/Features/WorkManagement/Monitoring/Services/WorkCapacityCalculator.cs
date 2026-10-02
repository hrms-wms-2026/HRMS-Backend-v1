using System.Text.Json;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.WorkManagement.Monitoring.Services;

/// <summary>A project's working time: productive hours per working day and which weekdays are
/// working days. Comes from the project's owning Legal Entity (work start/end time, break, standard
/// working days); holidays are not considered.</summary>
public sealed record WorkCalendar(decimal DailyHours, IReadOnlySet<DayOfWeek> WorkingDays)
{
    public static WorkCalendar Default { get; } = new(WorkCapacityCalculator.DefaultDailyHours, WorkCapacityCalculator.MondayToFriday);

    /// <summary>Working days from..to, both inclusive; 0 when to is before from.</summary>
    public int WorkingDaysBetween(DateOnly from, DateOnly to) => WorkCapacityCalculator.WorkingDays(from, to, WorkingDays);

    /// <summary>The most hours this many people can produce from..to (inclusive).</summary>
    public decimal Capacity(int people, DateOnly from, DateOnly to) => people * WorkingDaysBetween(from, to) * DailyHours;
}

/// <summary>Pure working-time arithmetic behind the project monitor.</summary>
public static class WorkCapacityCalculator
{
    /// <summary>Used when the Legal Entity has no (valid) work window configured.</summary>
    public const decimal DefaultDailyHours = 8m;

    public static IReadOnlySet<DayOfWeek> MondayToFriday { get; } = new HashSet<DayOfWeek>
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday
    };

    /// <summary>End minus start (an end at or before the start wraps past midnight) minus the break.
    /// Falls back to 8h when the window is unset or leaves no time.</summary>
    public static decimal DailyHours(TimeOnly? start, TimeOnly? end, int? breakMinutes)
    {
        if (start is null || end is null)
            return DefaultDailyHours;

        var minutes = (end.Value - start.Value).TotalMinutes - Math.Max(0, breakMinutes ?? 0);
        return minutes <= 0 ? DefaultDailyHours : Math.Round((decimal)minutes / 60m, 2);
    }

    public static int WorkingDays(DateOnly from, DateOnly to, IReadOnlySet<DayOfWeek> workingDays)
    {
        if (to < from)
            return 0;

        var total = to.DayNumber - from.DayNumber + 1;
        var fullWeeks = total / 7;
        var count = fullWeeks * workingDays.Count;
        for (var day = from.AddDays(fullWeeks * 7); day <= to; day = day.AddDays(1))
            if (workingDays.Contains(day.DayOfWeek))
                count++;
        return count;
    }

    /// <summary>Parses the Legal Entity's ISO weekday list ("[1,2,3,4,5]", 1 = Monday ... 7 = Sunday).
    /// Anything unreadable or empty means Monday to Friday.</summary>
    public static IReadOnlySet<DayOfWeek> ParseWorkingDays(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return MondayToFriday;
        try
        {
            var days = (JsonSerializer.Deserialize<int[]>(json) ?? [])
                .Where(n => n is >= 1 and <= 7)
                .Select(n => n == 7 ? DayOfWeek.Sunday : (DayOfWeek)n)
                .ToHashSet();
            return days.Count == 0 ? MondayToFriday : days;
        }
        catch (JsonException)
        {
            return MondayToFriday;
        }
    }

    public static WorkCalendar FromLegalEntity(LegalEntity? entity) => entity is null
        ? WorkCalendar.Default
        : new WorkCalendar(
            DailyHours(entity.WorkStartTime, entity.WorkEndTime, entity.BreakDurationMinutes),
            ParseWorkingDays(entity.StandardWorkingDays));
}
