using FluentValidation;
using ONEVO.Application.Features.Auth.Login.Validation;

namespace ONEVO.Application.Features.Auth.Login.Commands.BaseForgotPassword;

/// <summary>Task 19. Only the shape of the address is checked, never whether an account exists, so the endpoint
/// still answers the same way for known and unknown addresses.</summary>
public sealed class BaseForgotPasswordCommandValidator : AbstractValidator<BaseForgotPasswordCommand>
{
    public BaseForgotPasswordCommandValidator()
    {
        RuleFor(x => x.Email).ValidLoginEmail();
    }
}
