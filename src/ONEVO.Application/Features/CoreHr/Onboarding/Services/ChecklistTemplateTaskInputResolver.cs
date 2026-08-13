using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Onboarding.Models;
using ONEVO.Application.Features.CoreHr.Onboarding.ServiceInterfaces;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Services;

/// <summary>Structured checklist-task input carried by Create/Update commands - the "strict
/// task definition contract" the caller submits, before it becomes a stored
/// ChecklistTaskDefinition. AssignedToId and AssigneePositionId are mutually exclusive; exactly
/// one must be set unless OwnerType is "employee" (then neither may be set).</summary>
public sealed record ChecklistTemplateTaskInput(
    string Title, string OwnerType, Guid? AssignedToId, Guid? AssigneePositionId, int DueOffsetDays, int? Sequence, bool IsRequired);

/// <summary>Turns request-shaped task inputs into concrete ChecklistTaskDefinitions, resolving
/// any AssigneePositionId to a single concrete user via IChecklistTemplateAssigneeResolver.
/// Shared by CreateChecklistTemplateCommandHandler and UpdateChecklistTemplateCommandHandler so
/// the assignee/owner-type rule is defined exactly once.</summary>
public sealed class ChecklistTemplateTaskInputResolver(IChecklistTemplateAssigneeResolver assigneeResolver)
{
    public async Task<Result<IReadOnlyList<ChecklistTaskDefinition>>> ResolveAsync(
        Guid tenantId, IReadOnlyList<ChecklistTemplateTaskInput> inputs, CancellationToken ct)
    {
        var definitions = new List<ChecklistTaskDefinition>();
        foreach (var input in inputs)
        {
            if (string.IsNullOrWhiteSpace(input.Title))
                return Result<IReadOnlyList<ChecklistTaskDefinition>>.UnprocessableEntity("Every checklist task requires a title.");

            if (!ChecklistTaskOwnerTypes.All.Contains(input.OwnerType))
                return Result<IReadOnlyList<ChecklistTaskDefinition>>.UnprocessableEntity($"Unknown checklist task ownerType '{input.OwnerType}'.");

            if (input.DueOffsetDays < 0)
                return Result<IReadOnlyList<ChecklistTaskDefinition>>.UnprocessableEntity("dueOffsetDays must not be negative.");

            if (input.OwnerType == ChecklistTaskOwnerTypes.Employee)
            {
                if (input.AssignedToId is not null || input.AssigneePositionId is not null)
                    return Result<IReadOnlyList<ChecklistTaskDefinition>>.UnprocessableEntity(
                        "assignedToId/assigneePositionId must not be set when ownerType is 'employee' - it resolves to the new hire automatically at finalization.");

                definitions.Add(new ChecklistTaskDefinition(input.Title, input.OwnerType, null, input.DueOffsetDays, null, input.Sequence, input.IsRequired));
                continue;
            }

            if (input.AssignedToId is not null && input.AssigneePositionId is not null)
                return Result<IReadOnlyList<ChecklistTaskDefinition>>.UnprocessableEntity(
                    "Provide either assignedToId or assigneePositionId for a checklist task, not both.");

            Guid resolvedAssignedToId;
            if (input.AssignedToId is not null)
            {
                resolvedAssignedToId = input.AssignedToId.Value;
            }
            else if (input.AssigneePositionId is not null)
            {
                var resolved = await assigneeResolver.ResolveAsync(tenantId, input.AssigneePositionId.Value, ct);
                if (!resolved.IsSuccess)
                    return Result<IReadOnlyList<ChecklistTaskDefinition>>.Failure(resolved.Error!, resolved.StatusCode ?? 422);
                resolvedAssignedToId = resolved.Value;
            }
            else
            {
                return Result<IReadOnlyList<ChecklistTaskDefinition>>.UnprocessableEntity(
                    $"assignedToId or assigneePositionId is required when ownerType is '{input.OwnerType}'.");
            }

            definitions.Add(new ChecklistTaskDefinition(input.Title, input.OwnerType, resolvedAssignedToId, input.DueOffsetDays, null, input.Sequence, input.IsRequired));
        }
        return Result<IReadOnlyList<ChecklistTaskDefinition>>.Success(definitions);
    }
}
