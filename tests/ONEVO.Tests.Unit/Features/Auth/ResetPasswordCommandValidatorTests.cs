using FluentValidation.TestHelper;
using ONEVO.Application.Features.Auth.Login.Commands.ResetPassword;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class ResetPasswordCommandValidatorTests
{
    private readonly ResetPasswordCommandValidator _validator = new();

    [Fact]
    public void Token_Empty_FailsValidation()
    {
        var result = _validator.TestValidate(new ResetPasswordCommand("", "ValidPass1"));
        result.ShouldHaveValidationErrorFor(x => x.Token);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short1")]
    [InlineData("1234567")]
    public void NewPassword_ShorterThanMinimumLength_FailsValidation(string password)
    {
        var result = _validator.TestValidate(new ResetPasswordCommand("valid-token", password));
        result.ShouldHaveValidationErrorFor(x => x.NewPassword);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("12345678")]
    [InlineData("ValidPassword1")]
    public void NewPassword_AtLeastMinimumLength_PassesValidation(string password)
    {
        var result = _validator.TestValidate(new ResetPasswordCommand("valid-token", password));
        result.ShouldNotHaveValidationErrorFor(x => x.NewPassword);
    }
}
