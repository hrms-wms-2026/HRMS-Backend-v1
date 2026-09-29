using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;

public sealed record SprintResponse(
    Guid Id, Guid ProjectId, string Name, string? Goal, DateOnly? StartDate, DateOnly? EndDate,
    string Status, DateTimeOffset? CompletedAt, DateTimeOffset? AchievedAt, bool CanManage)
{
    public static SprintResponse From(Sprint s, bool canManage) => new(
        s.Id, s.ProjectId, s.Name, s.Goal, s.StartDate, s.EndDate, s.Status, s.CompletedAt, s.AchievedAt, canManage);
}
