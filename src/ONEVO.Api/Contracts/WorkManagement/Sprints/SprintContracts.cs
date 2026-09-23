namespace ONEVO.Api.Contracts.WorkManagement.Sprints;

public sealed record CreateSprintRequest(string Name, string? Goal);
public sealed record EditSprintRequest(string Name, string? Goal, DateOnly? StartDate, DateOnly? EndDate);

// Added ahead of the tasks that wire them up (StartSprintCommand / CompleteSprintCommand rework) -
// the API contracts are part of Task 1's response-shape foundation; the commands/handlers/controller
// actions that consume these are built in later tasks of this plan.
public sealed record StartSprintRequest(DateOnly StartDate, DateOnly EndDate, string? Goal);
public sealed record CompleteSprintRequest(string Disposition, Guid? TargetSprintId);

public sealed record SprintViewModel(
    Guid Id, Guid ProjectId, string Name, string? Goal, DateOnly? StartDate, DateOnly? EndDate,
    string Status, DateTimeOffset? CompletedAt, DateTimeOffset? AchievedAt, bool CanManage);

public static class SprintViewModelMapper
{
    public static SprintViewModel ToViewModel(this Application.Features.WorkManagement.Sprints.DTOs.Responses.SprintResponse dto) =>
        new(dto.Id, dto.ProjectId, dto.Name, dto.Goal, dto.StartDate, dto.EndDate, dto.Status, dto.CompletedAt, dto.AchievedAt, dto.CanManage);
}
