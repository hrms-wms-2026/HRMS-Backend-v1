using FluentValidation.TestHelper;
using ONEVO.Application.Features.Leave.Type.Commands.CreateLeaveType;
using ONEVO.Domain.Features.Leave.Common;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Type;

public class CreateLeaveTypeCommandValidatorTests
{
    private readonly CreateLeaveTypeCommandValidator _validator = new();

    private static CreateLeaveTypeCommand Valid() =>
        new("Annual Leave", "ANNUAL", null, "#3B82F6", IsPaid: true,
            ApplicableGender: LeaveGenderRestrictions.All, RequiresDocument: false,
            DocumentRequiredAfterHours: null, AcceptedDocumentTypes: []);

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

    [Theory]
    [InlineData("#12AB9F", true)]
    [InlineData("#12ab9f", true)]
    [InlineData("red", false)]
    [InlineData("", false)]
    [InlineData("#12AB9", false)]
    public void Color_must_be_hex(string color, bool valid)
    {
        var result = _validator.TestValidate(Valid() with { Color = color });
        if (valid) result.ShouldNotHaveValidationErrorFor(x => x.Color);
        else result.ShouldHaveValidationErrorFor(x => x.Color);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(8.0, true)]
    [InlineData(0.0, false)]
    public void DocumentRequiredAfterHours_must_be_positive_when_set(double? hours, bool valid)
    {
        var result = _validator.TestValidate(Valid() with
        {
            RequiresDocument = true,
            DocumentRequiredAfterHours = hours is null ? null : (decimal)hours.Value
        });
        if (valid) result.ShouldNotHaveValidationErrorFor(x => x.DocumentRequiredAfterHours);
        else result.ShouldHaveValidationErrorFor(x => x.DocumentRequiredAfterHours);
    }

    [Fact]
    public void InvalidGender_HasError()
    {
        var result = _validator.TestValidate(Valid() with { ApplicableGender = "other" });
        result.ShouldHaveValidationErrorFor(x => x.ApplicableGender);
    }
}
