using FluentValidation;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.PushTask;

public class PushTaskCommandValidator : AbstractValidator<PushTaskCommand>
{
    public PushTaskCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEqual(Guid.Empty).WithMessage("Task is required.");
        RuleFor(x => x.Percent).InclusiveBetween(0, 100).WithMessage("Percent must be between 0 and 100.");
        RuleFor(x => x.Reason).MaximumLength(1000).WithMessage("Reason must be 1000 characters or fewer.");
    }
}
