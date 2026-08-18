using FluentValidation;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskStatus;

public class CreateTaskStatusCommandValidator : AbstractValidator<CreateTaskStatusCommand>
{
    public CreateTaskStatusCommandValidator()
    {
        RuleFor(x => x.ObjectiveId).NotEqual(Guid.Empty).WithMessage("Objective is required.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).WithMessage("Name is required and must be 100 characters or fewer.");
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0).WithMessage("Display order must not be negative.");
        RuleFor(x => x.Visibility).Must(v => v is TaskStatusVisibilities.Public or TaskStatusVisibilities.Private)
            .WithMessage("Visibility must be public or private.");
    }
}
