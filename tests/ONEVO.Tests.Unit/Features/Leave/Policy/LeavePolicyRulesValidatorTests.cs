using FluentAssertions;
using FluentValidation.TestHelper;
using ONEVO.Application.Features.Leave.Policy.Commands.Common;
using ONEVO.Domain.Features.Leave.Common;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Policy;

public class LeavePolicyRulesValidatorTests
{
    private readonly LeavePolicyRulesValidator _validator = new();

    private static LeavePolicyRulesInput Valid() => PolicyTestHarness.Rules();

    [Fact]
    public void Valid_rules_pass() => _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(8785)]
    public void AmountHours_must_be_positive_and_at_most_a_year_of_hours(decimal hours) =>
        _validator.TestValidate(Valid() with { AmountHours = hours }).ShouldHaveValidationErrorFor(x => x.AmountHours);

    [Fact]
    public void AmountPeriod_must_be_month_or_year() =>
        _validator.TestValidate(Valid() with { AmountPeriod = "daily" }).ShouldHaveValidationErrorFor(x => x.AmountPeriod);

    [Fact]
    public void CarryForwardCap_cannot_be_negative() =>
        _validator.TestValidate(Valid() with { CarryForwardCapHours = -1m }).ShouldHaveValidationErrorFor(x => x.CarryForwardCapHours);

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    public void CarryForwardExpiry_must_be_1_to_24_months(int months) =>
        _validator.TestValidate(Valid() with { CarryForwardExpiryMonths = months })
            .ShouldHaveValidationErrorFor(x => x.CarryForwardExpiryMonths);

    [Fact]
    public void After_months_requires_month_count()
    {
        _validator.TestValidate(Valid() with { EligibleFrom = LeaveEligibility.AfterMonths, EligibleAfterMonths = null })
            .ShouldHaveValidationErrorFor(x => x.EligibleAfterMonths);
        _validator.TestValidate(Valid() with { EligibleFrom = LeaveEligibility.AfterMonths, EligibleAfterMonths = 3 })
            .ShouldNotHaveValidationErrorFor(x => x.EligibleAfterMonths);
    }

    [Fact]
    public void Month_count_only_allowed_with_after_months() =>
        _validator.TestValidate(Valid() with { EligibleFrom = LeaveEligibility.Immediately, EligibleAfterMonths = 3 })
            .ShouldHaveValidationErrorFor(x => x.EligibleAfterMonths);

    [Fact]
    public void Notice_cannot_be_negative() =>
        _validator.TestValidate(Valid() with { MinimumNoticeDays = -1 }).ShouldHaveValidationErrorFor(x => x.MinimumNoticeDays);

    [Fact]
    public void MinRequestHours_must_be_positive() =>
        _validator.TestValidate(Valid() with { MinRequestHours = 0m }).ShouldHaveValidationErrorFor(x => x.MinRequestHours);

    [Fact]
    public void MaxRequestHours_cannot_be_below_min() =>
        _validator.TestValidate(Valid() with { MinRequestHours = 4m, MaxRequestHours = 2m })
            .ShouldHaveValidationErrorFor(x => x.MaxRequestHours);

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void TeamAbsence_must_be_1_to_100(decimal percent) =>
        _validator.TestValidate(Valid() with { MaxTeamAbsencePercent = percent })
            .ShouldHaveValidationErrorFor(x => x.MaxTeamAbsencePercent);

    [Fact]
    public void ApprovalMode_accepts_none() =>
        _validator.TestValidate(Valid() with { ApprovalMode = LeaveApprovalModes.None }).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void ApprovalMode_rejects_unknown() =>
        _validator.TestValidate(Valid() with { ApprovalMode = "boss" }).ShouldHaveValidationErrorFor(x => x.ApprovalMode);

    [Fact]
    public void Blackout_end_cannot_be_before_start()
    {
        var rules = Valid() with
        {
            Blackouts = [new LeavePolicyBlackoutInput(new DateOnly(2026, 12, 31), new DateOnly(2026, 12, 20), null)]
        };
        _validator.TestValidate(rules).IsValid.Should().BeFalse();
    }
}
