using ONEVO.Application.Features.CoreHr.Onboarding.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Onboarding.Models;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Services;

/// <summary>Company -> department -> position scope validation, shared by
/// Create/UpdateChecklistTemplateCommandHandler so the rule is defined exactly once.</summary>
public static class ChecklistTemplateScopeValidator
{
    public static async Task<string?> ValidateAsync(
        Guid tenantId, Guid legalEntityId, Guid? departmentId, Guid? positionId,
        IDepartmentRepository departmentRepository, IPositionRepository positionRepository, CancellationToken ct)
    {
        if (positionId is not null)
        {
            var position = await positionRepository.GetByIdForLegalEntityAsync(tenantId, legalEntityId, positionId.Value, ct);
            if (position is null || !position.IsActive)
                return "The selected position does not exist, is inactive, or does not belong to the selected legal entity.";
            if (departmentId is not null && position.DepartmentId != departmentId)
                return "The selected position does not belong to the selected department.";
            return null;
        }

        if (departmentId is not null)
        {
            var department = await departmentRepository.GetByIdForLegalEntityAsync(tenantId, legalEntityId, departmentId.Value, ct);
            if (department is null || !department.IsActive)
                return "The selected department does not exist, is inactive, or does not belong to the selected legal entity.";
        }

        return null;
    }
}

public static class ChecklistTemplateResponseMapper
{
    public static ChecklistTemplateResponse ToResponse(ChecklistTemplate template)
        => ToResponse(template, ChecklistTaskJsonContract.Parse(template.TasksJson, ChecklistTaskDueRuleMode.OffsetDays));

    public static ChecklistTemplateResponse ToResponse(ChecklistTemplate template, IReadOnlyList<ChecklistTaskDefinition> tasks)
        => new(
            template.Id, template.Name, template.TemplateType, template.LegalEntityId!.Value, template.DepartmentId, template.PositionId, template.IsActive,
            tasks.Select(t => new ChecklistTemplateTaskResponse(t.Title, t.OwnerType, t.AssignedToId, t.DueOffsetDays!.Value, t.Sequence, t.IsRequired)).ToList());

    public static ChecklistTemplateListItemResponse ToListItemResponse(ChecklistTemplate template)
    {
        var taskCount = ChecklistTaskJsonContract.Parse(template.TasksJson, ChecklistTaskDueRuleMode.OffsetDays).Count;
        return new(template.Id, template.Name, template.TemplateType, template.LegalEntityId!.Value, template.DepartmentId, template.PositionId, template.IsActive, taskCount);
    }
}
