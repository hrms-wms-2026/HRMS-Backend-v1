using FluentValidation;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.EditObjective;

public class EditObjectiveCommandValidator : AbstractValidator<EditObjectiveCommand>
{
    public EditObjectiveCommandValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(255).WithMessage("Title must be 255 characters or fewer.");

        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate).WithMessage("End date must not be earlier than start date.");

        RuleFor(x => x.AllocatedHours)
            .GreaterThanOrEqualTo(0).WithMessage("Allocated hours must not be negative.");
    }
}
