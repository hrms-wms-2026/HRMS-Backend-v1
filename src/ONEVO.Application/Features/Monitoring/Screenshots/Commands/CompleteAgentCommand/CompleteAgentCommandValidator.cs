using FluentValidation;

namespace ONEVO.Application.Features.Monitoring.Screenshots.Commands.CompleteAgentCommand;

public class CompleteAgentCommandValidator : AbstractValidator<CompleteAgentCommandCommand>
{
    public CompleteAgentCommandValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty();
        RuleFor(x => x.FileRecordId)
            .NotEmpty()
            .When(x => x.Success)
            .WithMessage("FileRecordId is required when Success is true.");
    }
}
