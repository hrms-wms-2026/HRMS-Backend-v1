using FluentValidation;

namespace ONEVO.Application.Features.WorkManagement.Projects.Commands.EditProject;

public class EditProjectCommandValidator : AbstractValidator<EditProjectCommand>
{
    public EditProjectCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Project name is required.")
            .MaximumLength(200).WithMessage("Project name must be 200 characters or fewer.");

        RuleFor(x => x.CategoryId)
            .NotEqual(Guid.Empty).WithMessage("Category is required.");

        RuleFor(x => x.TargetDate)
            .GreaterThanOrEqualTo(x => x.StartDate).WithMessage("Target date must not be earlier than start date.");

        RuleFor(x => x.Color)
            .MaximumLength(20).WithMessage("Color must be 20 characters or fewer.")
            .When(x => x.Color is not null);

        RuleFor(x => x.ActualHours)
            .GreaterThanOrEqualTo(0).WithMessage("Actual hours must not be negative.")
            .When(x => x.ActualHours is not null);
    }
}
