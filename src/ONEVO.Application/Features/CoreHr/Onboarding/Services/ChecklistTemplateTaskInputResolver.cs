using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Onboarding.Models;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Services;

/// <summary>Structured checklist-task input carried by Create/Update commands. For a reusable
/// template, OwnerType "employee" stores no assignee. Any other owner type stores a position
/// (AssigneePositionId) and must not require a concrete person. AssignedToId is still accepted
/// for backward compatibility but is mutually exclusive with AssigneePositionId.</summary>
public sealed record ChecklistTemplateTaskInput(
    string Title, string OwnerType, Guid? AssignedToId, Guid? AssigneePositionId, int DueOffsetDays, int? Sequence, bool IsRequired, bool IsBypassable = false, string? BypassPenaltyDescription = null, string? Category = null,
    string? Description = null, string? Priority = null, IReadOnlyList<Guid>? AttachmentFileIds = null);

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

            var detailsError = ValidateDetails(input);
            if (detailsError is not null)
                return Result<IReadOnlyList<ChecklistTaskDefinition>>.UnprocessableEntity(detailsError);

            if (input.OwnerType == ChecklistTaskOwnerTypes.Employee)
            {
                if (input.AssignedToId is not null || input.AssigneePositionId is not null)
                    return Result<IReadOnlyList<ChecklistTaskDefinition>>.UnprocessableEntity(
                        "A task assigned to the new employee cannot also name another person or position.");

                definitions.Add(WithDetails(input, new ChecklistTaskDefinition(input.Title, input.OwnerType, null, input.DueOffsetDays, null, input.Sequence, input.IsRequired)));
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

                definitions.Add(WithDetails(input, new ChecklistTaskDefinition(
                    input.Title, input.OwnerType, null, input.DueOffsetDays, null, input.Sequence, input.IsRequired,
                    AssigneePositionId: input.AssigneePositionId)));
                continue;
            }

            if (input.AssignedToId is not null)
            {
                definitions.Add(WithDetails(input, new ChecklistTaskDefinition(input.Title, input.OwnerType, input.AssignedToId, input.DueOffsetDays, null, input.Sequence, input.IsRequired)));
                continue;
            }

            return Result<IReadOnlyList<ChecklistTaskDefinition>>.UnprocessableEntity(
                "A task assigned to another person needs a position.");
        }
        return Result<IReadOnlyList<ChecklistTaskDefinition>>.Success(definitions);
    }

    public const int MaxDescriptionLength = 20_000;
    public const int MaxBypassPenaltyLength = 500;
    public const int MaxCategoryLength = 40;
    public const int MaxAttachmentsPerTask = 20;

    private static readonly IReadOnlySet<string> Priorities = new HashSet<string>
    {
        WorkTaskPriorities.Low, WorkTaskPriorities.Medium, WorkTaskPriorities.High, WorkTaskPriorities.Critical
    };

    private static string? ValidateDetails(ChecklistTemplateTaskInput input)
    {
        if (input.Description is { Length: > MaxDescriptionLength })
            return $"A checklist task description must be {MaxDescriptionLength} characters or fewer.";
        if (!string.IsNullOrWhiteSpace(input.Priority) && !Priorities.Contains(input.Priority))
            return "Unknown checklist task priority.";
        if (input.BypassPenaltyDescription is { Length: > MaxBypassPenaltyLength })
            return $"A bypass penalty note must be {MaxBypassPenaltyLength} characters or fewer.";
        if (input.Category is { Length: > MaxCategoryLength })
            return "Unknown checklist task category.";
        if (input.AttachmentFileIds is { Count: > MaxAttachmentsPerTask })
            return $"A checklist task can have at most {MaxAttachmentsPerTask} attachments.";
        return null;
    }

    private static ChecklistTaskDefinition WithDetails(ChecklistTemplateTaskInput input, ChecklistTaskDefinition definition)
        => definition with
        {
            IsBypassable = input.IsBypassable,
            BypassPenaltyDescription = input.IsBypassable && !string.IsNullOrWhiteSpace(input.BypassPenaltyDescription)
                ? input.BypassPenaltyDescription.Trim()
                : null,
            Category = string.IsNullOrWhiteSpace(input.Category) ? null : input.Category.Trim(),
            Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description,
            Priority = string.IsNullOrWhiteSpace(input.Priority) ? null : input.Priority,
            AttachmentFileIds = input.AttachmentFileIds is { Count: > 0 } ids ? ids.Distinct().ToList() : null,
        };
}
