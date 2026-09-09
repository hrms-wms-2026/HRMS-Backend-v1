using ONEVO.Application.Common.Helpers;

namespace ONEVO.Application.Features.Leave.Request.Helpers;

public sealed record LeaveRequestHourCalculationInput(
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    TimeOnly WorkStart,
    TimeOnly WorkEnd,
    int BreakMinutes,
    IReadOnlyCollection<int> StandardWorkingDays,
    IReadOnlyCollection<DateOnly> HolidayDates);

public sealed record LeaveRequestHourCalculationResult(
    decimal TotalHours,
    IReadOnlyList<DateOnly> CountedShiftStartDates);

public sealed class LeaveRequestHourCalculator
{
    public LeaveRequestHourCalculationResult Calculate(LeaveRequestHourCalculationInput input)
    {
        if (input.EndAt <= input.StartAt)
            return new(0m, []);

        var workDayHours = WorkDayHoursCalculator.Compute(input.WorkStart, input.WorkEnd, input.BreakMinutes);
        var working = input.StandardWorkingDays.ToHashSet();
        var holidays = input.HolidayDates.ToHashSet();
        var counted = new List<DateOnly>();
        decimal total = 0m;

        var fromDate = DateOnly.FromDateTime(input.StartAt.UtcDateTime);
        var toDate = DateOnly.FromDateTime(input.EndAt.UtcDateTime);
        if (input.WorkEnd <= input.WorkStart)
            fromDate = fromDate.AddDays(-1);

        for (var date = fromDate; date <= toDate; date = date.AddDays(1))
        {
            var iso = date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;
            if (!working.Contains(iso) || holidays.Contains(date))
                continue;

            var (shiftStart, shiftEnd) = WorkDayHoursCalculator.ShiftInterval(date, input.WorkStart, input.WorkEnd);
            var shiftStartDto = new DateTimeOffset(DateTime.SpecifyKind(shiftStart, DateTimeKind.Utc));
            var shiftEndDto = new DateTimeOffset(DateTime.SpecifyKind(shiftEnd, DateTimeKind.Utc));

            var overlapStart = input.StartAt > shiftStartDto ? input.StartAt : shiftStartDto;
            var overlapEnd = input.EndAt < shiftEndDto ? input.EndAt : shiftEndDto;
            if (overlapEnd <= overlapStart)
                continue;

            var overlapHours = (decimal)(overlapEnd - overlapStart).TotalHours;
            var fullHours = (decimal)(shiftEndDto - shiftStartDto).TotalHours;
            total += overlapHours >= fullHours - 0.01m
                ? workDayHours
                : decimal.Round(overlapHours, 2, MidpointRounding.AwayFromZero);
            counted.Add(date);
        }

        return new(decimal.Round(total, 2, MidpointRounding.AwayFromZero), counted);
    }
}
