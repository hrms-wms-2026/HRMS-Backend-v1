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
}
