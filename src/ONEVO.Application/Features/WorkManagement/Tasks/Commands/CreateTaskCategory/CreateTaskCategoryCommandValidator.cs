using FluentValidation;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskCategory;

public class CreateTaskCategoryCommandValidator : AbstractValidator<CreateTaskCategoryCommand>
{
    public CreateTaskCategoryCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEqual(Guid.Empty).WithMessage("Project is required.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).WithMessage("Name is required and must be 100 characters or fewer.");
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0).WithMessage("Display order must not be negative.");
    }
}
