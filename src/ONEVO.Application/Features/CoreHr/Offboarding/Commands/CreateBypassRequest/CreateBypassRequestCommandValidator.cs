using FluentValidation;
using ONEVO.Application.Common.Validation;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.CreateBypassRequest;

/// <summary>Task 19. offboarding_task_bypass_requests: bypass_reason nvarchar(500) required, penalty_description
/// nvarchar(500).</summary>
public sealed class CreateBypassRequestCommandValidator : AbstractValidator<CreateBypassRequestCommand>
{
    public const int TextMaxLength = 500;

    public CreateBypassRequestCommandValidator()
    {
        RuleFor(x => x.BypassReason).ValidName(TextMaxLength);
        RuleFor(x => x.PenaltyDescription).ValidName(TextMaxLength, required: false);
    }
}
