using FluentValidation;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ReorderTaskCategories;

public class ReorderTaskCategoriesCommandValidator : AbstractValidator<ReorderTaskCategoriesCommand>
{
    public ReorderTaskCategoriesCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEqual(Guid.Empty);
        RuleFor(x => x.Updates).NotEmpty();
        RuleForEach(x => x.Updates).NotNull()
            .WithMessage("Updates must not contain null entries.");
        RuleForEach(x => x.Updates)
            .Where(update => update is not null)
            .ChildRules(update =>
            {
                update.RuleFor(u => u.DisplayOrder).GreaterThanOrEqualTo(0);
            });
    }
}
