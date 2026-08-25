using FluentValidation.TestHelper;
using ONEVO.Application.Features.Leave.Policy.Commands.CreateLeavePolicy;
using ONEVO.Domain.Features.Leave.Common;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Policy;

public class CreateLeavePolicyCommandValidatorTests
{
    private readonly CreateLeavePolicyCommandValidator _validator = new();

    private static CreateLeavePolicyCommand Valid() => new(
        "LK Policy",
        "Sri Lanka annual leave policy",
        "LK",
        null,
        LeaveAccrualMethods.Annual,
        LeaveAccrualStarts.Immediately,
        null,
        LeaveProrationMethods.CalendarDays,
        false,
        0,
        null,
        7,
        14,
        0.5m,
        20m,
        LeaveApprovalModes.AnyOne,
        new DateOnly(2026, 1, 1),
        [new LeavePolicyTypeRuleInput(Guid.NewGuid(), 20m, null, 5m, 3)],
        [new LeavePolicyBlackoutPeriodInput(new DateOnly(2026, 12, 24), new DateOnly(2026, 12, 26), "Peak closure")],
        [Guid.NewGuid()],
        false);

    [Fact]
    public void ValidCommand_HasNoErrors()
    {
        var result = _validator.TestValidate(Valid());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void EmptyCountry_HasError()
    {
        var result = _validator.TestValidate(Valid() with { Country = "" });
        result.ShouldHaveValidationErrorFor(x => x.Country);
    }

    [Fact]
    public void NoLeaveTypes_HasError()
    {
        var result = _validator.TestValidate(Valid() with { LeaveTypes = [] });
        result.ShouldHaveValidationErrorFor(x => x.LeaveTypes);
    }

    [Fact]
    public void DuplicateLeaveTypes_HasError()
    {
        var leaveTypeId = Guid.NewGuid();
        var result = _validator.TestValidate(Valid() with
        {
            LeaveTypes =
            [
                new LeavePolicyTypeRuleInput(leaveTypeId, 20m, null, null, null),
                new LeavePolicyTypeRuleInput(leaveTypeId, 10m, null, null, null)
            ]
        });
        result.ShouldHaveValidationErrorFor(x => x.LeaveTypes);
    }

    [Fact]
    public void MonthlyAccrualMethod_RequiresMonthlyAccrualDays()
    {
        var result = _validator.TestValidate(Valid() with
        {
            AccrualMethod = LeaveAccrualMethods.Monthly,
            LeaveTypes = [new LeavePolicyTypeRuleInput(Guid.NewGuid(), 0m, null, null, null)]
        });

        result.ShouldHaveValidationErrorFor(x => x.LeaveTypes);
    }

    [Fact]
    public void BlackoutEndBeforeStart_HasError()
    {
        var result = _validator.TestValidate(Valid() with
        {
            BlackoutPeriods =
            [
                new LeavePolicyBlackoutPeriodInput(new DateOnly(2026, 12, 26), new DateOnly(2026, 12, 24), null)
            ]
        });

        result.ShouldHaveValidationErrorFor("BlackoutPeriods[0].EndDate");
    }
}
