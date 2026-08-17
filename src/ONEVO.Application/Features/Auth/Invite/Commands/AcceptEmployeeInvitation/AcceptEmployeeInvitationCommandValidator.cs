using FluentValidation;
using ONEVO.Application.Features.Auth.Login.Validation;

namespace ONEVO.Application.Features.Auth.Invite.Commands.AcceptEmployeeInvitation;

public sealed class AcceptEmployeeInvitationCommandValidator : AbstractValidator<AcceptEmployeeInvitationCommand>
{
    public AcceptEmployeeInvitationCommandValidator()
    {
        RuleFor(x => x.RawToken).NotEmpty();
        // Password is optional at the validator level - a returning user (already has active
        // credentials from a prior invitation/account) accepts without submitting one. The
        // handler enforces "a password IS required" for a brand-new user; that decision needs
        // the loaded User row, which isn't available at validation time. When a password IS
        // submitted, the usual strength/confirmation rules still apply.
        RuleFor(x => x.Password).ApplyPasswordPolicy().When(x => !string.IsNullOrEmpty(x.Password));
        RuleFor(x => x.ConfirmPassword)
            .Equal(x => x.Password)
            .WithMessage("Password and confirmation must match.")
            .When(x => !string.IsNullOrEmpty(x.Password));
        RuleFor(x => x.Acceptances).NotNull();
    }
}
