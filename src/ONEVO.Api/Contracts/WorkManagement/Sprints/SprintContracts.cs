namespace ONEVO.Api.Contracts.WorkManagement.Sprints;

public sealed record CreateSprintRequest(string Name, DateOnly StartDate, DateOnly EndDate);
public sealed record EditSprintRequest(string Name, DateOnly StartDate, DateOnly EndDate);

public sealed record SprintViewModel(
    Guid Id, Guid ObjectiveId, string Name, DateOnly StartDate, DateOnly EndDate, string Status,
    DateTimeOffset? CompletedAt, DateTimeOffset? AchievedAt);

public static class SprintViewModelMapper
{
    public static SprintViewModel ToViewModel(this Application.Features.WorkManagement.Sprints.DTOs.Responses.SprintResponse dto) =>
        new(dto.Id, dto.ObjectiveId, dto.Name, dto.StartDate, dto.EndDate, dto.Status, dto.CompletedAt, dto.AchievedAt);
}
