using FluentValidation;

namespace ONEVO.Application.Features.WorkManagement.Projects.Commands.CreateProjectCategory;

public class CreateProjectCategoryCommandValidator : AbstractValidator<CreateProjectCategoryCommand>
{
    public CreateProjectCategoryCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Category name is required.")
            .MaximumLength(100).WithMessage("Category name cannot exceed 100 characters.");
    }
}
