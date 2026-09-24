using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Leave.Calendar.Helpers;

public sealed record LeaveCalendarMonthRange(
    int Year,
    int Month,
    DateOnly MonthStart,
    DateOnly MonthEnd)
{
    public static Result<LeaveCalendarMonthRange> From(int year, int month)
    {
        if (month is < 1 or > 12)
            return Result<LeaveCalendarMonthRange>.Failure("Month must be between 1 and 12.");

        try
        {
            var start = new DateOnly(year, month, 1);
            var end = start.AddMonths(1).AddDays(-1);
            return Result<LeaveCalendarMonthRange>.Success(new LeaveCalendarMonthRange(year, month, start, end));
        }
        catch (ArgumentOutOfRangeException)
        {
            return Result<LeaveCalendarMonthRange>.Failure("Year is outside the supported calendar range.");
        }
    }

    public IEnumerable<DateOnly> Dates()
    {
        for (var date = MonthStart; date <= MonthEnd; date = date.AddDays(1))
            yield return date;
    }
}
