using FluentValidation.TestHelper;
using ONEVO.Application.Features.Auth.Login.Commands.ForcePasswordChange;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class ForcePasswordChangeCommandValidatorTests
{
    private readonly ForcePasswordChangeCommandValidator _validator = new();

    private static ForcePasswordChangeCommand ValidCommand(
        string current = "OldPassword1", string newPassword = "NewPassword1") => new(
        Email: "user@acme.test",
        CurrentPassword: current,
        NewPassword: newPassword,
        IpAddress: null,
        UserAgent: null);

    [Fact]
    public void Email_Empty_FailsValidation()
    {
        var result = _validator.TestValidate(ValidCommand() with { Email = "" });
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void CurrentPassword_Empty_FailsValidation()
    {
        var result = _validator.TestValidate(ValidCommand() with { CurrentPassword = "" });
        result.ShouldHaveValidationErrorFor(x => x.CurrentPassword);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short1")]
    public void NewPassword_ShorterThanMinimumLength_FailsValidation(string password)
    {
        var result = _validator.TestValidate(ValidCommand(newPassword: password));
        result.ShouldHaveValidationErrorFor(x => x.NewPassword);
    }

    [Fact]
    public void NewPassword_AtLeastMinimumLength_PassesValidation()
    {
        var result = _validator.TestValidate(ValidCommand());
        result.ShouldNotHaveValidationErrorFor(x => x.NewPassword);
    }

    [Fact]
    public void NewPassword_EqualToCurrentPassword_FailsValidation()
    {
        var result = _validator.TestValidate(ValidCommand(current: "SamePassword1", newPassword: "SamePassword1"));
        result.ShouldHaveValidationErrorFor(x => x.NewPassword);
    }

    [Fact]
    public void NewPassword_DifferentFromCurrentPassword_PassesValidation()
    {
        var result = _validator.TestValidate(ValidCommand(current: "OldPassword1", newPassword: "NewPassword1"));
        result.ShouldNotHaveValidationErrorFor(x => x.NewPassword);
    }
}
