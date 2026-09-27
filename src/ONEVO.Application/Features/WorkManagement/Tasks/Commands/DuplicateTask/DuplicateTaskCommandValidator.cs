using FluentValidation;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.DuplicateTask;

public class DuplicateTaskCommandValidator : AbstractValidator<DuplicateTaskCommand>
{
    public DuplicateTaskCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEqual(Guid.Empty).WithMessage("Task is required.");
        RuleFor(x => x.DestinationObjectiveId).NotEqual(Guid.Empty).WithMessage("Destination module is required.");
        RuleFor(x => x.Title).NotEmpty().MaximumLength(500).WithMessage("Title is required and must be 500 characters or fewer.");
    }
}
