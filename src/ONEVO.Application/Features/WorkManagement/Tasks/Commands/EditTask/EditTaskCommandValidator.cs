using FluentValidation;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTask;

public class EditTaskCommandValidator : AbstractValidator<EditTaskCommand>
{
    public EditTaskCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEqual(Guid.Empty).WithMessage("Task is required.");
        RuleFor(x => x.Title).NotEmpty().MaximumLength(500).WithMessage("Title is required and must be 500 characters or fewer.");
        RuleFor(x => x.Priority).Must(p => p is WorkTaskPriorities.Low or WorkTaskPriorities.Medium or WorkTaskPriorities.High or WorkTaskPriorities.Critical)
            .WithMessage("Priority must be low, medium, high, or critical.");
                RuleFor(x => x.EstimatedHours).GreaterThanOrEqualTo(0).When(x => x.EstimatedHours.HasValue)
            .WithMessage("Estimated hours must not be negative.");
        RuleFor(x => x.ProgressPercent).InclusiveBetween(0, 100).When(x => x.ProgressPercent.HasValue)
            .WithMessage("Progress percent must be between 0 and 100.");
        RuleFor(x => x.Reason).MaximumLength(1000)
            .WithMessage("Reason must be 1000 characters or fewer.");

    }
}
