using ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;

namespace ONEVO.Api.Contracts.WorkManagement.CalendarEvents;

public sealed record CreateCalendarEventRequest(
    string Name, string Color, DateOnly StartDate, DateOnly EndDate,
    List<Guid> ObjectiveIds, List<Guid> TaskIds);

public sealed record UpdateCalendarEventRequest(
    string? Name, string? Color, DateOnly? StartDate, DateOnly? EndDate,
    List<Guid>? ObjectiveIds, List<Guid>? TaskIds);

public sealed record ProjectCalendarEventLinkViewModel(
    Guid EventId,
    string EventName,
    string EventColor,
    DateOnly EventStartDate,
    DateOnly EventEndDate,
    string Membership,
    int TasksInEventCount,
    int TaskTotalCount);

public sealed record ProjectCalendarModuleViewModel(
    Guid ObjectiveId,
    Guid ProjectId,
    Guid? ParentObjectiveId,
    string Title,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsActive,
    bool IsAchieved,
    bool CanEdit,
    IReadOnlyList<ProjectCalendarEventLinkViewModel> Events);

public sealed record ProjectCalendarEventBandViewModel(
    Guid EventId,
    string Name,
    string Color,
    DateOnly StartDate,
    DateOnly EndDate,
    bool CanEdit);

public sealed record ProjectCalendarViewModel(
    IReadOnlyList<ProjectCalendarModuleViewModel> Modules,
    IReadOnlyList<ProjectCalendarEventBandViewModel> Bands);

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
    public static ProjectCalendarViewModel ToViewModel(this ProjectCalendarResponse response)
        => new(
            response.Modules.Select(m => new ProjectCalendarModuleViewModel(
                m.ObjectiveId, m.ProjectId, m.ParentObjectiveId, m.Title,
                m.StartDate, m.EndDate, m.IsActive, m.IsAchieved, m.CanEdit,
                m.Events.Select(e => new ProjectCalendarEventLinkViewModel(
                    e.EventId, e.EventName, e.EventColor, e.EventStartDate, e.EventEndDate,
                    e.Membership, e.TasksInEventCount, e.TaskTotalCount)).ToList())).ToList(),
            response.Bands.Select(b => new ProjectCalendarEventBandViewModel(
                b.EventId, b.Name, b.Color, b.StartDate, b.EndDate, b.CanEdit)).ToList());

    public static CalendarEventViewModel ToViewModel(this CalendarEventResponse response)
        => new(response.Id, response.ProjectId, response.Name, response.Color, response.Status,
            response.StartDate, response.EndDate, response.ObjectiveIds, response.TaskIds,
            response.CreatedAt, response.ArchivedById, response.ArchivedAt);
}
