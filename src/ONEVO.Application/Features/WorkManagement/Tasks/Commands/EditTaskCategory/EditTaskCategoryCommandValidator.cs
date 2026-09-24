using FluentValidation;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTaskCategory;

public class EditTaskCategoryCommandValidator : AbstractValidator<EditTaskCategoryCommand>
{
    public EditTaskCategoryCommandValidator()
    {
        RuleFor(x => x.CategoryId).NotEqual(Guid.Empty).WithMessage("Task category is required.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).WithMessage("Name is required and must be 100 characters or fewer.");
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0).WithMessage("Display order must not be negative.");
    }
}
