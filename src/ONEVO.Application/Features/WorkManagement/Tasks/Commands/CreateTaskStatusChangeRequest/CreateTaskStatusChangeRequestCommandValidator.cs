using FluentValidation;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskStatusChangeRequest;

public class CreateTaskStatusChangeRequestCommandValidator : AbstractValidator<CreateTaskStatusChangeRequestCommand>
{
    public CreateTaskStatusChangeRequestCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEqual(Guid.Empty).WithMessage("Project is required.");
        RuleFor(x => x.Note).MaximumLength(1000);
        RuleFor(x => x.Changes).NotNull().WithMessage("Changes are required.");
        RuleFor(x => x.Changes).Must(c => c is null || TaskStatusChangeSetRules.Validate(c) is null)
            .WithMessage(x => TaskStatusChangeSetRules.Validate(x.Changes) ?? "Invalid changes.");
    }
}

/// <summary>Shape checks on a change set, shared by the validator and the handler (which re-runs
/// them because tests call Handle without the MediatR validation pipeline).</summary>
public static class TaskStatusChangeSetRules
{
    public static string? Validate(TaskStatusChangeSet? changes)
    {
        if (changes is null || changes.Adds is null || changes.Updates is null || changes.Deletes is null
            || changes.BaseOrder is null || changes.Order is null)
            return "Changes are required.";

        if (changes.IsEmpty)
            return "There are no changes to request.";

        foreach (var add in changes.Adds)
        {
            if (add is null || string.IsNullOrWhiteSpace(add.TempKey) || Guid.TryParse(add.TempKey, out _))
                return "Each new status needs a non-Guid temporary key.";
            var error = ValidateFields(add.Name, add.Category, add.Color, add.Visibility);
            if (error is not null) return error;
        }

        if (changes.Adds.Select(a => a.TempKey).Distinct().Count() != changes.Adds.Count)
            return "Temporary keys must be unique.";

        foreach (var update in changes.Updates)
        {
            if (update is null || update.StatusId == Guid.Empty || update.From is null || update.To is null)
                return "Each status update needs a status and its before/after values.";
            var error = ValidateFields(update.To.Name, update.To.Category, update.To.Color, update.To.Visibility);
            if (error is not null) return error;
        }

        if (changes.Deletes.Any(d => d is null || d.StatusId == Guid.Empty))
            return "Each deleted status must be identified.";

        var updateIds = changes.Updates.Select(u => u.StatusId).ToList();
        var deleteIds = changes.Deletes.Select(d => d.StatusId).ToList();
        if (updateIds.Distinct().Count() != updateIds.Count || deleteIds.Distinct().Count() != deleteIds.Count
            || updateIds.Intersect(deleteIds).Any())
            return "A status can be updated or deleted at most once per request.";

        foreach (var key in changes.Adds.Select(a => a.TempKey))
        {
            if (changes.Order.Count(k => k == key) != 1)
                return "Every new status must appear exactly once in the requested order.";
        }

        return null;
    }

    private static string? ValidateFields(string? name, string? category, string? color, string? visibility)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
            return "Status names are required and must be 100 characters or fewer.";
        if (category is not (TaskStatusCategories.NotStarted or TaskStatusCategories.Active or TaskStatusCategories.Done))
            return "Category must be not_started, active, or done.";
        if (visibility is not (TaskStatusVisibilities.Public or TaskStatusVisibilities.Private))
            return "Visibility must be public or private.";
        if (color is null || !System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}$"))
            return "Color must be a 6-digit hex code, e.g. #2563EB.";
        return null;
    }
}
