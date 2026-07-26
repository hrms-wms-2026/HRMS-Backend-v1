using FluentValidation;

namespace ONEVO.Application.Features.Auth.Invite.Commands.AcceptInvitationPassword;

public sealed class AcceptInvitationPasswordCommandValidator : AbstractValidator<AcceptInvitationPasswordCommand>
{
    public AcceptInvitationPasswordCommandValidator()
    {
        RuleFor(x => x.RawToken).NotEmpty();
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8)
            .WithMessage("Password must be at least 8 characters.");
        RuleFor(x => x.ConfirmPassword).NotEmpty();
        RuleFor(x => x.ConfirmPassword)
            .Equal(x => x.Password)
            .WithMessage("Password and confirmation must match.");
        RuleFor(x => x.Acceptances).NotNull();
    }
}
