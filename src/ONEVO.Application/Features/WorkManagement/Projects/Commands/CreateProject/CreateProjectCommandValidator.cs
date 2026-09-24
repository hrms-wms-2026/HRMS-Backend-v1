using FluentValidation;

namespace ONEVO.Application.Features.WorkManagement.Projects.Commands.CreateProject;

public class CreateProjectCommandValidator : AbstractValidator<CreateProjectCommand>
{
    public CreateProjectCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Project name is required.")
            .MaximumLength(200).WithMessage("Project name must be 200 characters or fewer.");

        RuleFor(x => x.Identifier)
            .NotEmpty().WithMessage("Project identifier is required.")
            .MaximumLength(20).WithMessage("Project identifier must be 20 characters or fewer.")
            .Matches("^[A-Za-z][A-Za-z0-9]*$").WithMessage("Project identifier must start with a letter and contain only letters and digits.");

        RuleFor(x => x.CategoryId)
            .NotEqual(Guid.Empty).WithMessage("Category is required.");

        RuleFor(x => x.TargetDate)
            .GreaterThanOrEqualTo(x => x.StartDate).WithMessage("Target date must not be earlier than start date.");

        RuleFor(x => x.ActualHours)
            .GreaterThanOrEqualTo(0).WithMessage("Actual hours must not be negative.")
            .When(x => x.ActualHours is not null);

        RuleFor(x => x.DefaultObjectiveAllocatedHours)
            .GreaterThanOrEqualTo(0).WithMessage("Allocated hours must not be negative.");

        RuleForEach(x => x.Labels).ChildRules(label =>
        {
            label.RuleFor(l => l.Name)
                .NotEmpty().WithMessage("Label name is required.")
                .MaximumLength(50).WithMessage("Label name must be 50 characters or fewer.");
            label.RuleFor(l => l.Color)
                .NotEmpty().WithMessage("Label color is required.")
                .MaximumLength(20).WithMessage("Label color must be 20 characters or fewer.");
        });

        RuleFor(x => x.Labels)
            .Must(labels => labels
                .Select(l => l.Name.Trim().ToLowerInvariant())
                .Distinct()
                .Count() == labels.Count)
            .WithMessage("Duplicate label names are not allowed in the same request.");
    }
}
