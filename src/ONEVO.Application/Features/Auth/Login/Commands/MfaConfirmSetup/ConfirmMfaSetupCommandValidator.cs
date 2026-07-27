using FluentValidation;

namespace ONEVO.Application.Features.Auth.Login.Commands.MfaConfirmSetup;

public class ConfirmMfaSetupCommandValidator : AbstractValidator<ConfirmMfaSetupCommand>
{
    public ConfirmMfaSetupCommandValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Code is required.")
            .Matches("^[0-9]{6}$").WithMessage("Code must be 6 digits.");
    }
}
