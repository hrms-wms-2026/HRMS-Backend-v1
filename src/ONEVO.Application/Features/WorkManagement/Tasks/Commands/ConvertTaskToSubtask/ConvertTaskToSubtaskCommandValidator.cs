using FluentValidation;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ConvertTaskToSubtask;

public class ConvertTaskToSubtaskCommandValidator : AbstractValidator<ConvertTaskToSubtaskCommand>
{
    public ConvertTaskToSubtaskCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEqual(Guid.Empty).WithMessage("Task is required.");
        RuleFor(x => x.NewParentTaskId).NotEqual(Guid.Empty).WithMessage("Target task is required.");
    }
}
