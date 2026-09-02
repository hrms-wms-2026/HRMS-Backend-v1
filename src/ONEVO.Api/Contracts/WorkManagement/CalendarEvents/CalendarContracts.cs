using ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;

namespace ONEVO.Api.Contracts.WorkManagement.CalendarEvents;

public sealed record CreateCalendarEventRequest(
    string Name, string Color, DateOnly StartDate, DateOnly EndDate,
    List<Guid> ObjectiveIds, List<Guid> TaskIds);

public sealed record UpdateCalendarEventRequest(
    string? Name, string? Color, DateOnly? StartDate, DateOnly? EndDate,
    List<Guid>? ObjectiveIds, List<Guid>? TaskIds);

public sealed record ProjectCalendarItemViewModel(
    Guid ObjectiveId,
    Guid ProjectId,
    Guid? ParentObjectiveId,
    string Title,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsActive,
    bool IsAchieved,
    bool CanEdit,
    Guid? CalendarEventId,
    string? CalendarEventColor);

public sealed record CalendarEventViewModel(
    Guid Id,
    Guid ProjectId,
    string Name,
    string Color,
    string Status,
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlyList<Guid> ObjectiveIds,
    IReadOnlyList<Guid> TaskIds,
    DateTimeOffset CreatedAt,
    Guid? ArchivedById,
    DateTimeOffset? ArchivedAt);

public static class CalendarViewModelMapper
{
    public static ProjectCalendarItemViewModel ToViewModel(this ProjectCalendarItemResponse response)
        => new(response.ObjectiveId, response.ProjectId, response.ParentObjectiveId, response.Title,
            response.StartDate, response.EndDate, response.IsActive, response.IsAchieved, response.CanEdit,
            response.CalendarEventId, response.CalendarEventColor);

    public static CalendarEventViewModel ToViewModel(this CalendarEventResponse response)
        => new(response.Id, response.ProjectId, response.Name, response.Color, response.Status,
            response.StartDate, response.EndDate, response.ObjectiveIds, response.TaskIds,
            response.CreatedAt, response.ArchivedById, response.ArchivedAt);
}
