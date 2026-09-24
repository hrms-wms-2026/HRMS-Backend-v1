namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

public sealed record ObjectiveHistoryItemResponse(
    Guid ObjectiveId, string Title, Guid ProjectId, bool IsAchieved, DateTimeOffset? RemovedAt);
