using FluentAssertions;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Domain.Features.Leave.Common;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Entitlement;

public class LeaveEntitlementCalculatorTests
{
    private static readonly int[] FixtureWorkingDays = [1, 2, 3, 4, 5];

    [Fact]
    public void Calculate_CalendarProration_MatchesProductWorkedExampleInclusive()
    {
        var calculator = new LeaveEntitlementCalculator(new LeaveWorkingDayCounter());

        var result = calculator.Calculate(ValidInput(
            Year: 2026,
            HireDate: new DateOnly(2026, 7, 1),
            AnnualEntitlementDays: 20m,
            AsOfDate: new DateOnly(2026, 8, 21)));

        result.TotalDays.Should().Be(10.1m);
        result.CarriedForwardDays.Should().Be(0m);
        result.SkipReason.Should().BeNull();
    }

    [Fact]
    public void Calculate_LeapYear_StillDividesBy365()
    {
        var calculator = new LeaveEntitlementCalculator(new LeaveWorkingDayCounter());

        var result = calculator.Calculate(ValidInput(
            Year: 2028,
            HireDate: new DateOnly(2028, 7, 1),
            AnnualEntitlementDays: 20m,
            AsOfDate: new DateOnly(2028, 7, 1)));

        result.TotalDays.Should().Be(10.1m);
    }

    [Fact]
    public void Calculate_CarryForward_UsesConfiguredPolicyCap()
    {
        var calculator = new LeaveEntitlementCalculator(new LeaveWorkingDayCounter());

        var result = calculator.Calculate(ValidInput(
            Year: 2027,
            HireDate: new DateOnly(2025, 2, 1),
            AnnualEntitlementDays: 20m,
            PreviousYearRemainingDays: 8m,
            CarryForwardMaxDays: 5m,
            CarryForwardExpiryMonths: 3,
            AsOfDate: new DateOnly(2027, 1, 1)));

        result.CarriedForwardDays.Should().Be(5m);
        result.ForfeitedDays.Should().Be(3m);
        result.TotalDays.Should().Be(20m);
        result.CarryForwardExpiresOn.Should().Be(new DateOnly(2027, 4, 1));
    }

    [Fact]
    public void Calculate_ExpiredCarryForward_IsZeroAndForfeitsRemaining()
    {
        var calculator = new LeaveEntitlementCalculator(new LeaveWorkingDayCounter());

        var result = calculator.Calculate(ValidInput(
            Year: 2027,
            HireDate: new DateOnly(2025, 2, 1),
            AnnualEntitlementDays: 20m,
            PreviousYearRemainingDays: 8m,
            CarryForwardMaxDays: 5m,
            CarryForwardExpiryMonths: 3,
            AsOfDate: new DateOnly(2027, 8, 21)));

        result.CarriedForwardDays.Should().Be(0m);
        result.ForfeitedDays.Should().Be(8m);
        result.CarryForwardExpiresOn.Should().Be(new DateOnly(2027, 4, 1));
    }

    [Fact]
    public void Calculate_UnlimitedCarryWithExpiry_KeepsPreviousRemaining()
    {
        var calculator = new LeaveEntitlementCalculator(new LeaveWorkingDayCounter());

        var result = calculator.Calculate(ValidInput(
            Year: 2027,
            HireDate: new DateOnly(2025, 2, 1),
            AnnualEntitlementDays: 20m,
            PreviousYearRemainingDays: 8m,
            CarryForwardMaxDays: null,
            CarryForwardExpiryMonths: 3,
            AsOfDate: new DateOnly(2027, 1, 1)));

        result.CarriedForwardDays.Should().Be(8m);
        result.ForfeitedDays.Should().Be(0m);
    }

    [Fact]
    public void Calculate_UsesNonFixturePolicyAmountFromInput()
    {
        var calculator = new LeaveEntitlementCalculator(new LeaveWorkingDayCounter());

        var result = calculator.Calculate(ValidInput(
            Year: 2026,
            HireDate: new DateOnly(2024, 1, 1),
            AnnualEntitlementDays: 17.5m,
            AsOfDate: new DateOnly(2026, 1, 1)));

        result.TotalDays.Should().Be(17.5m);
    }

    [Fact]
    public void Calculate_MonthlyAccrual_CountsMonthsThroughYearEnd()
    {
        var calculator = new LeaveEntitlementCalculator(new LeaveWorkingDayCounter());

        var result = calculator.Calculate(ValidInput(
            Year: 2026,
            HireDate: new DateOnly(2024, 1, 1),
            AnnualEntitlementDays: 24m,
            AccrualMethod: LeaveAccrualMethods.Monthly,
            AsOfDate: new DateOnly(2026, 3, 15)));

        result.TotalDays.Should().Be(24m);
    }

    [Fact]
    public void Calculate_MonthlyAccrual_MidYearHireProratesMonths()
    {
        var calculator = new LeaveEntitlementCalculator(new LeaveWorkingDayCounter());

        var result = calculator.Calculate(ValidInput(
            Year: 2026,
            HireDate: new DateOnly(2026, 7, 1),
            AnnualEntitlementDays: 24m,
            AccrualMethod: LeaveAccrualMethods.Monthly,
            AsOfDate: new DateOnly(2026, 8, 21)));

        result.TotalDays.Should().Be(12m);
    }

    [Fact]
    public void Calculate_FirstYearPercent_AppliesBeforeProration()
    {
        var calculator = new LeaveEntitlementCalculator(new LeaveWorkingDayCounter());

        var result = calculator.Calculate(ValidInput(
            Year: 2026,
            HireDate: new DateOnly(2026, 1, 1),
            AnnualEntitlementDays: 20m,
            FirstYearReducedPercent: 50m,
            AsOfDate: new DateOnly(2026, 1, 1)));

        result.TotalDays.Should().Be(10m);
    }

    [Fact]
    public void Calculate_MissingHireDate_Skips()
    {
        var calculator = new LeaveEntitlementCalculator(new LeaveWorkingDayCounter());

        var result = calculator.Calculate(ValidInput(
            Year: 2026,
            HireDate: new DateOnly(1, 1, 1),
            AnnualEntitlementDays: 20m,
            AsOfDate: new DateOnly(2026, 1, 1)));

        result.SkipReason.Should().Be("No hire date");
        result.TotalDays.Should().Be(0m);
    }

    [Fact]
    public void CountWorkingDays_UsesConfiguredLegalEntityWorkingDays()
    {
        var count = new LeaveWorkingDayCounter().CountWorkingDays(
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 7),
            [2, 4]);

        count.Should().Be(2);
    }

    private static LeaveEntitlementCalculationInput ValidInput(
        int Year,
        DateOnly HireDate,
        decimal AnnualEntitlementDays,
        DateOnly AsOfDate,
        decimal PreviousYearRemainingDays = 0m,
        decimal? CarryForwardMaxDays = null,
        int? CarryForwardExpiryMonths = null,
        string AccrualMethod = LeaveAccrualMethods.Annual,
        string AccrualStart = LeaveAccrualStarts.Immediately,
        int? AccrualAfterNMonths = null,
        string ProrationMethod = LeaveProrationMethods.CalendarDays,
        bool ProbationRestriction = false,
        decimal? FirstYearReducedPercent = null,
        int MinimumTenureMonths = 0) => new(
            Year,
            HireDate,
            null,
            AnnualEntitlementDays,
            PreviousYearRemainingDays,
            CarryForwardMaxDays,
            CarryForwardExpiryMonths,
            AccrualMethod,
            AccrualStart,
            AccrualAfterNMonths,
            ProrationMethod,
            ProbationRestriction,
            FirstYearReducedPercent,
            MinimumTenureMonths,
            FixtureWorkingDays,
            AsOfDate);
}
