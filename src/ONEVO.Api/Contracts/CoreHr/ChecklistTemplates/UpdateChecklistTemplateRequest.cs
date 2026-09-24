namespace ONEVO.Api.Contracts.CoreHr.ChecklistTemplates;

/// <summary>TemplateType and LegalEntityId are intentionally absent - both are immutable after
/// creation (see UpdateChecklistTemplateCommandHandler's doc comment).</summary>
public sealed record UpdateChecklistTemplateRequest(
    string Name,
    Guid? DepartmentId,
    Guid? PositionId,
    IReadOnlyList<ChecklistTemplateTaskRequest> Tasks);
