using FluentValidation;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTaskStatus;

public class EditTaskStatusCommandValidator : AbstractValidator<EditTaskStatusCommand>
{
    public EditTaskStatusCommandValidator()
    {
        RuleFor(x => x.StatusId).NotEqual(Guid.Empty).WithMessage("Task status is required.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).WithMessage("Name is required and must be 100 characters or fewer.");
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0).WithMessage("Display order must not be negative.");
        RuleFor(x => x.Visibility).Must(v => v is TaskStatusVisibilities.Public or TaskStatusVisibilities.Private)
            .WithMessage("Visibility must be public or private.");
    }
}
