using FluentValidation;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTask;

public class CreateTaskCommandValidator : AbstractValidator<CreateTaskCommand>
{
    public CreateTaskCommandValidator()
    {
        RuleFor(x => x.ObjectiveId).NotEqual(Guid.Empty).WithMessage("Objective is required.");
        RuleFor(x => x.Title).NotEmpty().MaximumLength(500).WithMessage("Title is required and must be 500 characters or fewer.");
        RuleFor(x => x.TaskType).Must(t => t is WorkTaskTypes.Task or WorkTaskTypes.Bug or WorkTaskTypes.Story or WorkTaskTypes.Feature)
            .WithMessage("Task type must be task, bug, story, or feature.");
        RuleFor(x => x.Priority).Must(p => p is WorkTaskPriorities.Low or WorkTaskPriorities.Medium or WorkTaskPriorities.High or WorkTaskPriorities.Critical)
            .WithMessage("Priority must be low, medium, high, or critical.");
        RuleFor(x => x.EstimatedHours).GreaterThanOrEqualTo(0).When(x => x.EstimatedHours.HasValue)
            .WithMessage("Estimated hours must not be negative.");
        RuleFor(x => x.SprintId).NotEqual(Guid.Empty).WithMessage("Sprint is required.");
    }
}
