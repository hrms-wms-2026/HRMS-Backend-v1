using FluentValidation.TestHelper;
using ONEVO.Application.Features.Leave.Type.Commands.CreateLeaveType;
using ONEVO.Domain.Features.Leave.Common;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Type;

public class CreateLeaveTypeCommandValidatorTests
{
    private readonly CreateLeaveTypeCommandValidator _validator = new();

    private static CreateLeaveTypeCommand Valid() =>
        new("Annual Leave", "ANNUAL", null, LeaveTypeCategories.Annual,
            true, true, false, null, [], null, 20m, false, null, null, false,
            LeaveGenderRestrictions.All, 0);

    [Fact]
    public void ValidCommand_HasNoErrors()
    {
        var result = _validator.TestValidate(Valid());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void EmptyName_HasError()
    {
        var result = _validator.TestValidate(Valid() with { Name = "" });
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void InvalidCategory_HasError()
    {
        var result = _validator.TestValidate(Valid() with { Category = "vacation" });
        result.ShouldHaveValidationErrorFor(x => x.Category);
    }

    [Fact]
    public void CarryForwardExceedingDefaultDays_HasError()
    {
        var result = _validator.TestValidate(Valid() with
        {
            CarryForwardAllowed = true,
            DefaultDaysPerYear = 10m,
            MaxCarryForwardDays = 15m
        });
        result.ShouldHaveValidationErrorFor(x => x.MaxCarryForwardDays);
    }
}
