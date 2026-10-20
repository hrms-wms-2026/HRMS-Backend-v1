using ONEVO.Domain.Features.Leave.Common;

namespace ONEVO.Application.Features.Leave.Entitlement.Helpers;

public record LeaveEntitlementCalculationInput(
    int Year,
    DateOnly? HireDate,
    DateOnly? ProbationEndDate,
    decimal AnnualEntitlementDays,
    decimal PreviousYearRemainingDays,
    decimal? CarryForwardMaxDays,
    int? CarryForwardExpiryMonths,
    string AccrualMethod,
    string AccrualStart,
    int? AccrualAfterNMonths,
    string ProrationMethod,
    bool ProbationRestriction,
    decimal? FirstYearReducedPercent,
    int MinimumTenureMonths,
    IReadOnlyCollection<int> StandardWorkingDays,
    DateOnly AsOfDate);

public record LeaveEntitlementCalculationResult(
    decimal TotalDays,
    decimal CarriedForwardDays,
    decimal ForfeitedDays,
    bool ProbationRestrictionApplied,
    DateOnly? CarryForwardExpiresOn,
    string? SkipReason);

public class LeaveEntitlementCalculator
{
    public const int CalendarDaysPerYear = 365;

    private readonly ILeaveWorkingDayCounter _workingDays;

    public LeaveEntitlementCalculator(ILeaveWorkingDayCounter workingDays)
    {
        _workingDays = workingDays;
    }

    public static DateOnly? CarryForwardExpiryDate(int year, int? expiryMonths)
    {
        if (expiryMonths is null or <= 0)
            return null;

        return new DateOnly(year, 1, 1).AddMonths(expiryMonths.Value);
    }

    public static bool HasHireDate(DateOnly? hireDate) =>
        hireDate is { } value && value.Year >= 1900;

    public LeaveEntitlementCalculationResult Calculate(LeaveEntitlementCalculationInput input)
    {
        throw new NotImplementedException("Leave cleanup Part 2 Task 2");
    }

    private static DateOnly Max(DateOnly left, DateOnly right) => left >= right ? left : right;

    private static decimal RoundOneDecimal(decimal value) =>
        decimal.Round(value, 1, MidpointRounding.AwayFromZero);
}
