namespace ONEVO.Api.Contracts.CoreHr.ChecklistTemplates;

/// <summary>OwnerType "employee" sets neither AssignedToId nor AssigneePositionId. Another-person
/// template tasks set AssigneePositionId only (no concrete person). AssignedToId remains accepted
/// for backward compatibility and is mutually exclusive with AssigneePositionId.</summary>
public sealed record ChecklistTemplateTaskRequest(
    string Title,
    string OwnerType,
    Guid? AssignedToId,
    Guid? AssigneePositionId,
    int DueOffsetDays,
    int? Sequence,
    bool IsRequired);
