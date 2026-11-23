using FluentValidation;
using ONEVO.Application.Common.Validation;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.RejectBypassRequest;

/// <summary>Task 19. offboarding_task_bypass_requests.decision_comment is nvarchar(500).</summary>
public sealed class RejectBypassRequestCommandValidator : AbstractValidator<RejectBypassRequestCommand>
{
    public const int DecisionCommentMaxLength = 500;

    public RejectBypassRequestCommandValidator()
    {
        RuleFor(x => x.DecisionComment).ValidName(DecisionCommentMaxLength, required: false);
    }
}
