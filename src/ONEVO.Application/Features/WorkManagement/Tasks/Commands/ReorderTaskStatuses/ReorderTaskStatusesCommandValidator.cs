using FluentValidation;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ReorderTaskStatuses;

public class ReorderTaskStatusesCommandValidator : AbstractValidator<ReorderTaskStatusesCommand>
{
    public ReorderTaskStatusesCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEqual(Guid.Empty);
        RuleFor(x => x.Updates).NotEmpty();
        RuleForEach(x => x.Updates).NotNull()
            .WithMessage("Updates must not contain null entries.");
        RuleForEach(x => x.Updates)
            .Where(update => update is not null)
            .ChildRules(update =>
            {
                update.RuleFor(u => u.Visibility).Must(v => v is TaskStatusVisibilities.Public or TaskStatusVisibilities.Private);
                update.RuleFor(u => u.Category).Must(c => c is TaskStatusCategories.NotStarted or TaskStatusCategories.Active or TaskStatusCategories.Done);
                update.RuleFor(u => u.Color).NotEmpty().Matches("^#[0-9A-Fa-f]{6}$");
                update.RuleFor(u => u.DisplayOrder).GreaterThanOrEqualTo(0);
            });
        RuleFor(x => x.Updates).Must(updates =>
                updates is not null
                && updates.All(u => u is not null)
                && updates.Count(u => u.StatusId != Guid.Empty) == updates.Select(u => u.StatusId).Distinct().Count())
            .WithMessage("Updates must not contain duplicate status IDs.");
        RuleFor(x => x.Updates).Must(updates =>
                updates is not null
                && updates.All(u => u is not null)
                && updates.Count(u => u.Category == TaskStatusCategories.Done) <= 1)
            .WithMessage("At most one status in a single reorder call may be marked Done.");
    }
}

