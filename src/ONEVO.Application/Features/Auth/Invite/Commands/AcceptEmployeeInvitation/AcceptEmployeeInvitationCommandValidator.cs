using FluentValidation;
using ONEVO.Application.Features.Auth.Login.Validation;

namespace ONEVO.Application.Features.Auth.Invite.Commands.AcceptEmployeeInvitation;

public sealed class AcceptEmployeeInvitationCommandValidator : AbstractValidator<AcceptEmployeeInvitationCommand>
{
    public AcceptEmployeeInvitationCommandValidator()
    {
        RuleFor(x => x.RawToken).NotEmpty();
        RuleFor(x => x.Password).ApplyPasswordPolicy();
        RuleFor(x => x.ConfirmPassword).NotEmpty();
        RuleFor(x => x.ConfirmPassword)
            .Equal(x => x.Password)
            .WithMessage("Password and confirmation must match.");
        RuleFor(x => x.Acceptances).NotNull();
    }
}
