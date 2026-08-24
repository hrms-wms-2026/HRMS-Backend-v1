using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Models;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Domain.Lookups;

namespace ONEVO.Application.Features.CoreHr.OnboardingDraft.Services;

/// <summary>Validates OnboardingDraft.EditedTasksJson: employee tasks stay deferred, and
/// another-person tasks must name a concrete active user (UserId), not only a position.</summary>
public static class EditedOnboardingTasksValidator
{
    public static async Task<Result> ValidateAsync(
        Guid tenantId,
        string? editedTasksJson,
        IEmployeeRepository employees,
        IPositionAssignmentRepository assignments,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(editedTasksJson))
            return Result.Success();

        IReadOnlyList<ChecklistTaskDefinition> definitions;
        try
        {
            definitions = ChecklistTaskJsonContract.Parse(editedTasksJson, ChecklistTaskDueRuleMode.AbsoluteDate);
        }
        catch (ArgumentException)
        {
            return Result.Failure("Some checklist tasks are incomplete. Add a title, due date, and a specific person for any task assigned to someone other than the new employee.");
        }

        foreach (var definition in definitions)
        {
            if (definition.OwnerType == ChecklistTaskOwnerTypes.Employee)
                continue;

            if (definition.AssignedToId is null)
                return Result.Failure("Tasks assigned to another person must name a specific person.");

            var employee = await employees.GetDefaultForUserAsync(tenantId, definition.AssignedToId.Value, ct);
            if (employee is null || employee.EmploymentStatusId != EmploymentStatusIds.Active || employee.UserId == Guid.Empty)
                return Result.Failure("The selected assignee is not an active employee.");

            if (definition.AssigneePositionId is { } positionId)
            {
                var primary = await assignments.GetActivePrimaryAsync(tenantId, employee.Id, ct);
                if (primary is null || primary.PositionId != positionId)
                    return Result.Failure("The selected assignee is not currently in that position.");
            }
        }

        return Result.Success();
    }
}
