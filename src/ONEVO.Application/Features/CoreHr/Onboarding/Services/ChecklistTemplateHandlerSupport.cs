using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Onboarding.Models;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
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
    public static ChecklistTemplateResponse ToResponse(ChecklistTemplate template, IReadOnlyList<EntityAssetWithFile>? templateFiles = null)
        => ToResponse(template, ChecklistTaskJsonContract.Parse(template.TasksJson, ChecklistTaskDueRuleMode.OffsetDays), templateFiles);

    /// <summary>templateFiles = the template's checklist_template assets; a task's attachment ids
    /// that no longer resolve to one of them are left out of the response.</summary>
    public static ChecklistTemplateResponse ToResponse(
        ChecklistTemplate template, IReadOnlyList<ChecklistTaskDefinition> tasks, IReadOnlyList<EntityAssetWithFile>? templateFiles = null)
    {
        var filesById = (templateFiles ?? Array.Empty<EntityAssetWithFile>())
            .GroupBy(f => f.FileRecordId)
            .ToDictionary(g => g.Key, g => g.First());
        return new(
            template.Id, template.Name, template.TemplateType, template.LegalEntityId!.Value, template.DepartmentId, template.PositionId, template.IsActive,
            tasks.Select(t => new ChecklistTemplateTaskResponse(
                t.Title, t.OwnerType, t.AssignedToId, t.DueOffsetDays!.Value, t.Sequence, t.IsRequired, t.AssigneePositionId,
                t.IsBypassable, t.BypassPenaltyDescription, t.Category, t.Description, t.Priority,
                (t.AttachmentFileIds ?? Array.Empty<Guid>())
                    .Where(filesById.ContainsKey)
                    .Select(id => filesById[id])
                    .Select(f => new ChecklistTemplateTaskAttachmentResponse(f.FileRecordId, f.OriginalFileName, f.FileSizeBytes, f.ContentType))
                    .ToList())).ToList());
    }

    public static ChecklistTemplateListItemResponse ToListItemResponse(ChecklistTemplate template)
    {
        var taskCount = ChecklistTaskJsonContract.Parse(template.TasksJson, ChecklistTaskDueRuleMode.OffsetDays).Count;
        return new(template.Id, template.Name, template.TemplateType, template.LegalEntityId!.Value, template.DepartmentId, template.PositionId, template.IsActive, taskCount);
    }
}

/// <summary>Links a template to the attachment files its tasks reference and drops any task
/// attachment id that could not be linked (not the caller's pending upload and not already the
/// template's own file), so TasksJson never points at a file the template does not own.</summary>
public static class ChecklistTemplateAttachmentSync
{
    public static async Task<(IReadOnlyList<ChecklistTaskDefinition> Tasks, IReadOnlyList<EntityAssetWithFile> Files)> SyncAsync(
        Guid tenantId, Guid userId, Guid templateId, IReadOnlyList<ChecklistTaskDefinition> tasks,
        ITaskAssetLinker linker, IEntityAssetRepository assets, CancellationToken ct)
    {
        var desired = tasks.SelectMany(t => t.AttachmentFileIds ?? Array.Empty<Guid>()).Distinct().ToList();
        await linker.SyncChecklistTemplateAttachmentsAsync(tenantId, userId, templateId, desired, ct);

        var files = (await assets.ListByOwnerAsync(tenantId, EntityAssetOwnerTypes.ChecklistTemplate, templateId, ct))
            .Where(f => f.AssetPurpose == UploadPurposeCatalog.TaskAttachment)
            .ToList();
        var linked = files.Select(f => f.FileRecordId).ToHashSet();
        var filtered = tasks
            .Select(t => t.AttachmentFileIds is { Count: > 0 } ids
                ? t with { AttachmentFileIds = ids.Where(linked.Contains).ToList() is { Count: > 0 } kept ? kept : null }
                : t)
            .ToList();
        return (filtered, files);
    }
}
