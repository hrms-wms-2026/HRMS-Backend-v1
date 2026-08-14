namespace ONEVO.Api.Contracts.CoreHr.ChecklistTemplates;

/// <summary>Exactly one of AssignedToId/AssigneePositionId may be set unless OwnerType is
/// "employee" (then neither may be set - see ChecklistTemplateTaskInputResolver).</summary>
public sealed record ChecklistTemplateTaskRequest(
    string Title,
    string OwnerType,
    Guid? AssignedToId,
    Guid? AssigneePositionId,
    int DueOffsetDays,
    int? Sequence,
    bool IsRequired);
