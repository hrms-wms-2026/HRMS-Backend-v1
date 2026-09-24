using FluentValidation;

namespace ONEVO.Application.Features.Auth.Login.Commands.MfaDisable;

public class DisableMfaCommandValidator : AbstractValidator<DisableMfaCommand>
{
    public DisableMfaCommandValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("Current password is required to disable MFA.");
    }
}
