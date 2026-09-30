using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Helpers;

/// <summary>
/// The inclusive date range every period-aware employee Overview widget reads. Omitted bounds
/// default to the current calendar month; a range is capped at 12 months (366 days) so one
/// widget request cannot scan unbounded history.
/// </summary>
public sealed record EmployeePeriod(DateOnly From, DateOnly To)
{
    public const int MaxDays = 366;

    public static Result<EmployeePeriod> Resolve(DateOnly? from, DateOnly? to, DateOnly today)
    {
        if (from is null && to is null)
        {
            var first = new DateOnly(today.Year, today.Month, 1);
            var last = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
            return Result<EmployeePeriod>.Success(new EmployeePeriod(first, last));
        }

        if (from is null || to is null)
            return Result<EmployeePeriod>.Failure("from and to must be provided together.");

        if (from.Value > to.Value)
            return Result<EmployeePeriod>.Failure("from must be less than or equal to to.");

        if (to.Value.DayNumber - from.Value.DayNumber + 1 > MaxDays)
            return Result<EmployeePeriod>.Failure("The period cannot exceed 12 months.");

        return Result<EmployeePeriod>.Success(new EmployeePeriod(from.Value, to.Value));
    }

    /// <summary>The comparison window: the previous whole calendar month when this period is exactly
    /// one whole month, otherwise the same number of days ending the day before <see cref="From"/>.</summary>
    public EmployeePeriod Previous()
    {
        var isWholeMonth = From.Day == 1
            && From.Year == To.Year
            && From.Month == To.Month
            && To.Day == DateTime.DaysInMonth(To.Year, To.Month);

        if (isWholeMonth)
        {
            var first = From.AddMonths(-1);
            return new EmployeePeriod(first, new DateOnly(first.Year, first.Month, DateTime.DaysInMonth(first.Year, first.Month)));
        }

        var length = To.DayNumber - From.DayNumber + 1;
        var previousTo = From.AddDays(-1);
        return new EmployeePeriod(previousTo.AddDays(-(length - 1)), previousTo);
    }
}
