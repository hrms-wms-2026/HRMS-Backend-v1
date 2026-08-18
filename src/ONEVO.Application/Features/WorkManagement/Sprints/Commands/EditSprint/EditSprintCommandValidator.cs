using FluentValidation;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.EditSprint;

public class EditSprintCommandValidator : AbstractValidator<EditSprintCommand>
{
    public EditSprintCommandValidator()
    {
        RuleFor(x => x.SprintId).NotEqual(Guid.Empty);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x).Must(x => x.EndDate >= x.StartDate).WithMessage("End date must not be before start date.");
    }
}
