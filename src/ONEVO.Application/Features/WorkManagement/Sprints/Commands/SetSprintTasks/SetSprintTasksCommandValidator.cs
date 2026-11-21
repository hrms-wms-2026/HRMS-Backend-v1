using FluentValidation;
using ONEVO.Application.Common.Validation;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.SetSprintTasks;

/// <summary>Task 19. Each list is capped at 500 tasks per request: sprint planning moves far fewer tasks at once,
/// and the cap keeps one request from loading thousands of tasks.</summary>
public sealed class SetSprintTasksCommandValidator : AbstractValidator<SetSprintTasksCommand>
{
    public const int MaxTasksPerList = 500;

    public SetSprintTasksCommandValidator()
    {
        RuleFor(x => x.AddTaskIds).ValidIdList(MaxTasksPerList);
        RuleFor(x => x.RemoveTaskIds).ValidIdList(MaxTasksPerList);
    }
}
