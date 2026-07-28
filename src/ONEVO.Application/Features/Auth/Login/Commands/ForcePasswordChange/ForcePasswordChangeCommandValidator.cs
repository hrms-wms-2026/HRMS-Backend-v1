using FluentValidation;
using ONEVO.Application.Features.Auth.Login.Validation;

namespace ONEVO.Application.Features.Auth.Login.Commands.ForcePasswordChange;

public sealed class ForcePasswordChangeCommandValidator : AbstractValidator<ForcePasswordChangeCommand>
{
    public ForcePasswordChangeCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty();
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).ApplyPasswordPolicy();
        RuleFor(x => x.NewPassword)
            .NotEqual(x => x.CurrentPassword)
            .WithMessage("New password must be different from the current password.");
    }
}
