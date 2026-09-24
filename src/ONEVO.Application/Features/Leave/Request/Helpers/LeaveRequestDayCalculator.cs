namespace ONEVO.Application.Features.Leave.Request.Helpers;

public sealed record LeaveRequestDayCalculationInput(
    DateOnly StartDate,
    DateOnly EndDate,
    string? HalfDayPeriod,
    IReadOnlyCollection<int> StandardWorkingDays,
    IReadOnlyCollection<DateOnly> HolidayDates);

public sealed record LeaveRequestDayCalculationResult(
    decimal TotalDays,
    IReadOnlyList<DateOnly> CountedDates);

public sealed class LeaveRequestDayCalculator
{
    public LeaveRequestDayCalculationResult Calculate(LeaveRequestDayCalculationInput input)
    {
        if (input.EndDate < input.StartDate)
            return new LeaveRequestDayCalculationResult(0m, []);

        var workingDays = input.StandardWorkingDays.ToHashSet();
        var holidays = input.HolidayDates.ToHashSet();
        var countedDates = new List<DateOnly>();

        for (var date = input.StartDate; date <= input.EndDate; date = date.AddDays(1))
        {
            var isoDay = date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;
            if (!workingDays.Contains(isoDay) || holidays.Contains(date))
                continue;

            countedDates.Add(date);
        }

        decimal total = countedDates.Count;
        if (!string.IsNullOrWhiteSpace(input.HalfDayPeriod) && total > 0)
            total -= 0.5m;

        return new LeaveRequestDayCalculationResult(total, countedDates);
    }
}
