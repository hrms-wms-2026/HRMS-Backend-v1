using FluentValidation;

namespace ONEVO.Application.Features.Auth.Invite.Commands.AcceptInvitationGoogle;

public sealed class AcceptInvitationGoogleCommandValidator : AbstractValidator<AcceptInvitationGoogleCommand>
{
    public AcceptInvitationGoogleCommandValidator()
    {
        RuleFor(x => x.RawToken).NotEmpty();
        RuleFor(x => x.GoogleIdToken).NotEmpty();
        RuleFor(x => x.Acceptances).NotNull();
    }
}
