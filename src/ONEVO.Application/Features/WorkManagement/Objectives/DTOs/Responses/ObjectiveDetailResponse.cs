namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

public sealed record ObjectiveDetailResponse(
    Guid Id, Guid ProjectId, Guid? ParentObjectiveId, bool IsDefault, string Title, string? Description,
    Guid OwnerId, Guid? ReportingManagerId, Guid CreatedById, DateOnly StartDate, DateOnly EndDate,
    decimal Progress, decimal? ActualHours, decimal AllocatedHours, decimal CompletedHours,
    bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);
