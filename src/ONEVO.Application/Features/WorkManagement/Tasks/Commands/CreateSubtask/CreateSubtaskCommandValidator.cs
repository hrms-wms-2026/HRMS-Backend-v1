using FluentValidation;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateSubtask;

public sealed class CreateSubtaskCommandValidator : AbstractValidator<CreateSubtaskCommand>
{
    public CreateSubtaskCommandValidator()
    {
        RuleFor(x => x.ParentTaskId).NotEqual(Guid.Empty).WithMessage("Parent task is required.");
        RuleFor(x => x.Title).NotEmpty().MaximumLength(500)
            .WithMessage("Title is required and must be 500 characters or fewer.");
        RuleFor(x => x.Priority)
            .Must(priority => priority is null or WorkTaskPriorities.Low or WorkTaskPriorities.Medium
                or WorkTaskPriorities.High or WorkTaskPriorities.Critical)
            .WithMessage("Priority must be low, medium, high, or critical.");
    }
}
