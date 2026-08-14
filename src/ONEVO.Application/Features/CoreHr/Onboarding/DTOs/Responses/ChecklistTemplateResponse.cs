namespace ONEVO.Application.Features.CoreHr.Onboarding.DTOs.Responses;

public sealed record ChecklistTemplateTaskResponse(
    string Title, string OwnerType, Guid? AssignedToId, int DueOffsetDays, int? Sequence, bool IsRequired);

public sealed record ChecklistTemplateResponse(
    Guid Id, string Name, string TemplateType, Guid LegalEntityId, Guid? DepartmentId, Guid? PositionId,
    bool IsActive, IReadOnlyList<ChecklistTemplateTaskResponse> Tasks);

public sealed record ChecklistTemplateListItemResponse(
    Guid Id, string Name, string TemplateType, Guid LegalEntityId, Guid? DepartmentId, Guid? PositionId, bool IsActive, int TaskCount);

public sealed record ChecklistTemplateListResponse(IReadOnlyList<ChecklistTemplateListItemResponse> Items, int TotalCount, int Page, int PageSize);

public sealed record OnboardingChecklistTemplateMatchResponse(Guid Id, string Name, string MatchLevel, Guid? DepartmentId, Guid? PositionId);
