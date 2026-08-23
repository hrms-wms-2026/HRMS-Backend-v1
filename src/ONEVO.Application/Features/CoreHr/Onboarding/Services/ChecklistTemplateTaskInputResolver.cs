using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Onboarding.Models;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Services;

/// <summary>Structured checklist-task input carried by Create/Update commands. For a reusable
/// template, OwnerType "employee" stores no assignee. Any other owner type stores a position
/// (AssigneePositionId) and must not require a concrete person. AssignedToId is still accepted
/// for backward compatibility but is mutually exclusive with AssigneePositionId.</summary>
public sealed record ChecklistTemplateTaskInput(
    string Title, string OwnerType, Guid? AssignedToId, Guid? AssigneePositionId, int DueOffsetDays, int? Sequence, bool IsRequired);

/// <summary>Turns request-shaped task inputs into stored ChecklistTaskDefinitions without
/// resolving a position to a specific occupant. Shared by create/update template handlers.</summary>
public sealed class ChecklistTemplateTaskInputResolver(IPositionRepository positionRepository)
{
    public async Task<Result<IReadOnlyList<ChecklistTaskDefinition>>> ResolveAsync(
        Guid tenantId, Guid legalEntityId, IReadOnlyList<ChecklistTemplateTaskInput> inputs, CancellationToken ct)
    {
        var definitions = new List<ChecklistTaskDefinition>();
        foreach (var input in inputs)
        {
            if (string.IsNullOrWhiteSpace(input.Title))
                return Result<IReadOnlyList<ChecklistTaskDefinition>>.UnprocessableEntity("Every checklist task requires a title.");

            if (!ChecklistTaskOwnerTypes.All.Contains(input.OwnerType))
                return Result<IReadOnlyList<ChecklistTaskDefinition>>.UnprocessableEntity("Unknown checklist task assignee.");

            if (input.DueOffsetDays < 0)
                return Result<IReadOnlyList<ChecklistTaskDefinition>>.UnprocessableEntity("dueOffsetDays must not be negative.");

            if (input.OwnerType == ChecklistTaskOwnerTypes.Employee)
            {
                if (input.AssignedToId is not null || input.AssigneePositionId is not null)
                    return Result<IReadOnlyList<ChecklistTaskDefinition>>.UnprocessableEntity(
                        "A task assigned to the new employee cannot also name another person or position.");

                definitions.Add(new ChecklistTaskDefinition(input.Title, input.OwnerType, null, input.DueOffsetDays, null, input.Sequence, input.IsRequired));
                continue;
            }

            if (input.AssignedToId is not null && input.AssigneePositionId is not null)
                return Result<IReadOnlyList<ChecklistTaskDefinition>>.UnprocessableEntity(
                    "Provide a position or a specific person for a checklist task, not both.");

            if (input.AssigneePositionId is not null)
            {
                var position = await positionRepository.GetByIdForLegalEntityAsync(tenantId, legalEntityId, input.AssigneePositionId.Value, ct);
                if (position is null || !position.IsActive)
                    return Result<IReadOnlyList<ChecklistTaskDefinition>>.UnprocessableEntity(
                        "The selected assignee position does not exist, is inactive, or does not belong to this company.");

                definitions.Add(new ChecklistTaskDefinition(
                    input.Title, input.OwnerType, null, input.DueOffsetDays, null, input.Sequence, input.IsRequired,
                    AssigneePositionId: input.AssigneePositionId));
                continue;
            }

            if (input.AssignedToId is not null)
            {
                definitions.Add(new ChecklistTaskDefinition(input.Title, input.OwnerType, input.AssignedToId, input.DueOffsetDays, null, input.Sequence, input.IsRequired));
                continue;
            }

            return Result<IReadOnlyList<ChecklistTaskDefinition>>.UnprocessableEntity(
                "A task assigned to another person needs a position.");
        }
        return Result<IReadOnlyList<ChecklistTaskDefinition>>.Success(definitions);
    }
}
