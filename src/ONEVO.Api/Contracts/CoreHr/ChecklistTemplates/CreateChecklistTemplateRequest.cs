namespace ONEVO.Api.Contracts.CoreHr.ChecklistTemplates;

public sealed record CreateChecklistTemplateRequest(
    string Name,
    string TemplateType,
    Guid LegalEntityId,
    Guid? DepartmentId,
    Guid? PositionId,
    IReadOnlyList<ChecklistTemplateTaskRequest> Tasks);
