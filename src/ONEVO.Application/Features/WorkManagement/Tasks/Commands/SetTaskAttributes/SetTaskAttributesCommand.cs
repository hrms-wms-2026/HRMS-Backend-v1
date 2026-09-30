using FluentValidation;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.SetTaskAttributes;

/// <summary>
/// Narrow change of priority and/or due date. Unlike EditTaskCommand, this touches only the
/// named fields, so it is safe to apply to many tasks at once.
/// </summary>
public sealed record SetTaskAttributesCommand(
    Guid TaskId,
    string? Priority,
    bool SetDueDate,
    DateOnly? DueDate) : IRequest<Result>;

public sealed class SetTaskAttributesCommandValidator : AbstractValidator<SetTaskAttributesCommand>
{
    private static readonly string[] Priorities =
    {
        WorkTaskPriorities.Low,
        WorkTaskPriorities.Medium,
        WorkTaskPriorities.High,
        WorkTaskPriorities.Critical
    };

    public SetTaskAttributesCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty();
        RuleFor(x => x.Priority)
            .Must(p => p is null || Priorities.Contains(p))
            .WithMessage("Priority must be one of: low, medium, high, critical.");
        RuleFor(x => x)
            .Must(x => x.Priority is not null || x.SetDueDate)
            .WithMessage("Nothing to change.");
    }
}
