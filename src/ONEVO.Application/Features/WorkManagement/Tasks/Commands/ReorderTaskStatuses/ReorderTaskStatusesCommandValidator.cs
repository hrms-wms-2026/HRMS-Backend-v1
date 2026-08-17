using FluentValidation;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ReorderTaskStatuses;

public class ReorderTaskStatusesCommandValidator : AbstractValidator<ReorderTaskStatusesCommand>
{
    public ReorderTaskStatusesCommandValidator()
    {
        RuleFor(x => x.ObjectiveId).NotEqual(Guid.Empty);
        RuleFor(x => x.Updates).NotEmpty();
        RuleForEach(x => x.Updates).ChildRules(update =>
        {
            update.RuleFor(u => u.Visibility).Must(v => v is TaskStatusVisibilities.Public or TaskStatusVisibilities.Private);
            update.RuleFor(u => u.DisplayOrder).GreaterThanOrEqualTo(0);
        });
        RuleFor(x => x.Updates).Must(updates => updates.Count(u => u.MarksTaskComplete) == 1)
            .WithMessage("Exactly one status must be marked as the complete status.");
    }
}
