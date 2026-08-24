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
                update.RuleFor(u => u.DisplayOrder).GreaterThanOrEqualTo(0);
            });
        RuleFor(x => x.Updates).Must(updates =>
                updates is not null
                && updates.All(u => u is not null)
                && updates.Count(u => u.MarksTaskComplete) == 1)
            .WithMessage("Exactly one status must be marked as the complete status.");
    }
}
