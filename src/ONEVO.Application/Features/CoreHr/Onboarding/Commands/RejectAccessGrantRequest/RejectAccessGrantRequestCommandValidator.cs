using FluentValidation;
using ONEVO.Application.Common.Validation;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Commands.RejectAccessGrantRequest;

/// <summary>Task 19. access_grant_requests.decision_note is nvarchar(500).</summary>
public sealed class RejectAccessGrantRequestCommandValidator : AbstractValidator<RejectAccessGrantRequestCommand>
{
    public const int DecisionNoteMaxLength = 500;

    public RejectAccessGrantRequestCommandValidator()
    {
        RuleFor(x => x.DecisionNote).ValidName(DecisionNoteMaxLength, required: false);
    }
}
