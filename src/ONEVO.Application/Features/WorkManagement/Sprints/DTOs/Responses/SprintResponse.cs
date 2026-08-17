namespace ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;

public sealed record SprintResponse(
    Guid Id, Guid ObjectiveId, string Name, DateOnly StartDate, DateOnly EndDate, string Status,
    DateTimeOffset? CompletedAt, DateTimeOffset? AchievedAt);
