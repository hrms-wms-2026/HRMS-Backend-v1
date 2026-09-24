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
        if (!HasHireDate(input.HireDate))
            return new(0m, 0m, 0m, false, null, "No hire date");

        var yearStart = new DateOnly(input.Year, 1, 1);
        var yearEnd = new DateOnly(input.Year, 12, 31);
        var entitlementStart = ResolveEntitlementStart(input, yearStart);
        var probationApplied = input.ProbationRestriction && entitlementStart > yearStart;

        if (entitlementStart > yearEnd)
        {
            var reason = input.ProbationRestriction || input.AccrualStart == LeaveAccrualStarts.AfterProbation
                ? "Probation restriction applied"
                : "Entitlement start is after the selected year";
            return new(0m, 0m, 0m, probationApplied, null, reason);
        }

        var annualBase = input.AnnualEntitlementDays;
        if (input.FirstYearReducedPercent is { } percent && input.HireDate!.Value.Year == input.Year)
            annualBase = RoundOneDecimal(annualBase * percent / 100m);

        var annual = CalculateAnnualPortion(input, annualBase, entitlementStart, yearStart, yearEnd);
        var (carry, forfeited, expiresOn) = CalculateCarryForward(input);

        return new(annual, carry, forfeited, probationApplied, expiresOn, null);
    }

    private static DateOnly ResolveEntitlementStart(LeaveEntitlementCalculationInput input, DateOnly yearStart)
    {
        var hireDate = input.HireDate!.Value;
        var start = hireDate > yearStart ? hireDate : yearStart;

        if ((input.AccrualStart == LeaveAccrualStarts.AfterProbation || input.ProbationRestriction)
            && input.ProbationEndDate is { } probationEnd)
        {
            start = Max(start, probationEnd.AddDays(1));
        }

        if (input.AccrualStart == LeaveAccrualStarts.AfterNMonths && input.AccrualAfterNMonths is { } months)
            start = Max(start, hireDate.AddMonths(months));

        if (input.MinimumTenureMonths > 0)
            start = Max(start, hireDate.AddMonths(input.MinimumTenureMonths));

        return start;
    }

    private decimal CalculateAnnualPortion(
        LeaveEntitlementCalculationInput input,
        decimal annualBase,
        DateOnly entitlementStart,
        DateOnly yearStart,
        DateOnly yearEnd)
    {
        if (input.AccrualMethod == LeaveAccrualMethods.Monthly)
            return CalculateMonthlyPortion(annualBase, entitlementStart, yearEnd);

        if (entitlementStart <= yearStart)
            return RoundOneDecimal(annualBase);

        if (input.ProrationMethod == LeaveProrationMethods.WorkingDays)
        {
            var remaining = _workingDays.CountWorkingDays(entitlementStart, yearEnd, input.StandardWorkingDays);
            var total = _workingDays.CountWorkingDays(yearStart, yearEnd, input.StandardWorkingDays);
            return total == 0 ? 0m : RoundOneDecimal(annualBase * remaining / total);
        }

        var remainingCalendarDays = yearEnd.DayNumber - entitlementStart.DayNumber + 1;
        return RoundOneDecimal(annualBase * remainingCalendarDays / CalendarDaysPerYear);
    }

    private static decimal CalculateMonthlyPortion(
        decimal annualBase,
        DateOnly entitlementStart,
        DateOnly yearEnd)
    {
        if (yearEnd < entitlementStart)
            return 0m;

        var months = ((yearEnd.Year - entitlementStart.Year) * 12) + yearEnd.Month - entitlementStart.Month + 1;
        var monthlyAmount = annualBase / 12m;
        return RoundOneDecimal(monthlyAmount * months);
    }

    private static (decimal Carry, decimal Forfeited, DateOnly? ExpiresOn) CalculateCarryForward(
        LeaveEntitlementCalculationInput input)
    {
        var expiresOn = CarryForwardExpiryDate(input.Year, input.CarryForwardExpiryMonths);
        if (input.CarryForwardExpiryMonths is null or <= 0)
            return (0m, Math.Max(0m, input.PreviousYearRemainingDays), null);

        if (expiresOn is { } expiry && input.AsOfDate >= expiry)
            return (0m, Math.Max(0m, input.PreviousYearRemainingDays), expiry);

        var capped = input.CarryForwardMaxDays is { } max && max > 0
            ? Math.Min(input.PreviousYearRemainingDays, max)
            : input.PreviousYearRemainingDays;

        var carry = RoundOneDecimal(Math.Max(0m, capped));
        var forfeited = RoundOneDecimal(Math.Max(0m, input.PreviousYearRemainingDays - carry));
        return (carry, forfeited, expiresOn);
    }

    private static DateOnly Max(DateOnly left, DateOnly right) => left >= right ? left : right;

    private static decimal RoundOneDecimal(decimal value) =>
        decimal.Round(value, 1, MidpointRounding.AwayFromZero);
}
