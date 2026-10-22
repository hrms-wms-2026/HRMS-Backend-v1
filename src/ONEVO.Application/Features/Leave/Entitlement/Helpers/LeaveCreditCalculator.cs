using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Policy.Entities;

namespace ONEVO.Application.Features.Leave.Entitlement.Helpers;

public sealed record LeaveMonthPolicy(int Month, LeavePolicyVersion Version, Guid PolicyId);

public sealed record LeaveCreditInput(
    int Year,
    DateOnly HireDate,
    DateOnly? ProbationEndDate,
    DateOnly AsOf,
    IReadOnlyList<LeaveMonthPolicy> MonthPolicies);

public sealed record LeaveMonthValue(
    int Month,
    decimal Hours,
    Guid PolicyId,
    Guid VersionId,
    DateOnly CreditDate);

/// <summary>Pure calendar-month credit calculation. Persistence decides which past months are
/// frozen and which policy applies to each month; this calculator only evaluates eligibility,
/// due dates and values.</summary>
public static class LeaveCreditCalculator
{
    public static DateOnly EligibilityDate(
        LeavePolicyVersion version,
        DateOnly hireDate,
        DateOnly? probationEndDate) => version.EligibleFrom switch
    {
        LeaveEligibility.AfterProbation => probationEndDate?.AddDays(1) ?? hireDate,
        LeaveEligibility.AfterMonths => hireDate.AddMonths(version.EligibleAfterMonths ?? 0),
        _ => hireDate
    };

    public static IReadOnlyList<LeaveMonthValue> DueMonths(LeaveCreditInput input)
    {
        if (input.HireDate.Year > input.Year || input.AsOf.Year < input.Year)
            return [];

        var result = new List<LeaveMonthValue>();
        foreach (var monthPolicy in input.MonthPolicies.OrderBy(x => x.Month))
        {
            if (monthPolicy.Month is < 1 or > 12)
                throw new ArgumentOutOfRangeException(nameof(input), "Policy month must be between 1 and 12.");

            var version = monthPolicy.Version;
            var eligibility = EligibilityDate(version, input.HireDate, input.ProbationEndDate);
            if (!MonthCounts(input.Year, monthPolicy.Month, eligibility))
                continue;

            var monthStart = new DateOnly(input.Year, monthPolicy.Month, 1);
            DateOnly creditDate;
            decimal hours;
            if (version.AmountPeriod == LeaveAmountPeriods.Month)
            {
                creditDate = eligibility.Year == input.Year && eligibility.Month == monthPolicy.Month
                    ? eligibility
                    : monthStart;
                hours = version.AmountHours;
            }
            else
            {
                creditDate = eligibility.Year == input.Year && eligibility > new DateOnly(input.Year, 1, 1)
                    ? eligibility
                    : new DateOnly(input.Year, 1, 1);
                hours = version.AmountHours / 12m;
            }

            if (creditDate <= input.AsOf)
            {
                result.Add(new LeaveMonthValue(
                    monthPolicy.Month,
                    hours,
                    monthPolicy.PolicyId,
                    version.Id,
                    creditDate));
            }
        }

        return result;
    }

    private static bool MonthCounts(int year, int month, DateOnly eligibility)
    {
        if (eligibility.Year < year)
            return true;
        if (eligibility.Year > year || eligibility.Month > month)
            return false;
        if (eligibility.Month < month)
            return true;
        return eligibility.Day <= 15;
    }
}
