using FluentAssertions;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Policy.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Entitlement;

public class LeaveCreditCalculatorTests
{
    private static readonly Guid PolicyId = Guid.NewGuid();

    [Fact]
    public void Year_period_full_year_on_jan_1() =>
        Sum(Input(new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1), Version(112m, LeaveAmountPeriods.Year)))
            .Should().Be(112m);

    [Theory]
    [InlineData("2026-03-01", 24)]
    [InlineData("2026-02-28", 16)]
    public void Month_period_credits_only_reached_months(string asOf, decimal expected) =>
        Sum(Input(new DateOnly(2025, 1, 1), DateOnly.Parse(asOf), Version(8m, LeaveAmountPeriods.Month)))
            .Should().Be(expected);

    [Fact]
    public void Hire_on_15th_counts_month() =>
        Sum(Input(new DateOnly(2026, 4, 15), new DateOnly(2026, 4, 15), Version(8m, LeaveAmountPeriods.Month)))
            .Should().Be(8m);

    [Fact]
    public void Hire_on_16th_skips_month()
    {
        var version = Version(8m, LeaveAmountPeriods.Month);
        Sum(Input(new DateOnly(2026, 4, 16), new DateOnly(2026, 4, 30), version)).Should().Be(0m);
        Sum(Input(new DateOnly(2026, 4, 16), new DateOnly(2026, 5, 1), version)).Should().Be(8m);
    }

    [Fact]
    public void Year_period_mid_year_joiner_prorated() =>
        Sum(Input(new DateOnly(2026, 7, 10), new DateOnly(2026, 7, 10), Version(120m, LeaveAmountPeriods.Year)))
            .Should().Be(60m);

    [Fact]
    public void After_probation_delays_eligibility() =>
        Sum(Input(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
                Version(8m, LeaveAmountPeriods.Month, LeaveEligibility.AfterProbation), new DateOnly(2026, 3, 31)))
            .Should().Be(72m);

    [Fact]
    public void After_probation_null_end_treated_as_hire() =>
        Sum(Input(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
                Version(8m, LeaveAmountPeriods.Month, LeaveEligibility.AfterProbation)))
            .Should().Be(96m);

    [Fact]
    public void After_months_delays_eligibility() =>
        Sum(Input(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
                Version(8m, LeaveAmountPeriods.Month, LeaveEligibility.AfterMonths, 3)))
            .Should().Be(72m);

    [Theory]
    [InlineData(180, 130)]
    [InlineData(60, 110)]
    public void Year_period_mid_year_version_change(decimal newAmount, decimal expected)
    {
        var oldVersion = Version(120m, LeaveAmountPeriods.Year);
        var newVersion = Version(newAmount, LeaveAmountPeriods.Year);
        var policies = Enumerable.Range(1, 12)
            .Select(month => new LeaveMonthPolicy(month, month < 11 ? oldVersion : newVersion, PolicyId))
            .ToList();

        Sum(new LeaveCreditInput(2026, new DateOnly(2025, 1, 1), null,
            new DateOnly(2026, 12, 31), policies)).Should().Be(expected);
    }

    [Fact]
    public void Month_without_policy_contributes_nothing()
    {
        var version = Version(8m, LeaveAmountPeriods.Month);
        var policies = Enumerable.Range(6, 7).Select(m => new LeaveMonthPolicy(m, version, PolicyId)).ToList();
        Sum(new LeaveCreditInput(2026, new DateOnly(2025, 1, 1), null,
            new DateOnly(2026, 12, 31), policies)).Should().Be(56m);
    }

    [Fact]
    public void Future_hire_returns_zero() =>
        Sum(Input(new DateOnly(2027, 1, 1), new DateOnly(2026, 12, 31), Version(8m, LeaveAmountPeriods.Month)))
            .Should().Be(0m);

    private static LeaveCreditInput Input(
        DateOnly hireDate,
        DateOnly asOf,
        LeavePolicyVersion version,
        DateOnly? probationEnd = null) =>
        new(2026, hireDate, probationEnd, asOf,
            Enumerable.Range(1, 12).Select(m => new LeaveMonthPolicy(m, version, PolicyId)).ToList());

    private static decimal Sum(LeaveCreditInput input) =>
        decimal.Round(LeaveCreditCalculator.DueMonths(input).Sum(x => x.Hours), 2, MidpointRounding.AwayFromZero);

    private static LeavePolicyVersion Version(
        decimal amount,
        string period,
        string eligibleFrom = LeaveEligibility.Immediately,
        int? eligibleAfterMonths = null) => new()
    {
        Id = Guid.NewGuid(),
        LeavePolicyId = PolicyId,
        AmountHours = amount,
        AmountPeriod = period,
        EligibleFrom = eligibleFrom,
        EligibleAfterMonths = eligibleAfterMonths
    };
}
