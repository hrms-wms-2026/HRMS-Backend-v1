using ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;

namespace ONEVO.Api.Contracts.WorkManagement.CalendarEvents;

public sealed record CreateCalendarEventRequest(
    string Name, string Color, DateOnly StartDate, DateOnly EndDate,
    List<Guid> ObjectiveIds, List<Guid> TaskIds, string? Description = null);

public sealed record UpdateCalendarEventRequest(
    string? Name, string? Color, DateOnly? StartDate, DateOnly? EndDate,
    List<Guid>? ObjectiveIds, List<Guid>? TaskIds, string? Description = null);

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
    IReadOnlyList<ProjectCalendarEventLinkViewModel> Events,
    int? ProgressPercent);

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
    string? Description,
    IReadOnlyList<Guid> ObjectiveIds,
    IReadOnlyList<Guid> TaskIds,
    DateTimeOffset CreatedAt,
    Guid? ArchivedById,
    DateTimeOffset? ArchivedAt);

public sealed record CalendarEventDetailViewModel(
    Guid Id, Guid ProjectId, string Name, string Color, string Status,
    DateOnly StartDate, DateOnly EndDate, string? Description,
    Guid CreatedById, string? CreatedByName,
    IReadOnlyList<Guid> ObjectiveIds, IReadOnlyList<Guid> TaskIds,
    DateTimeOffset CreatedAt, Guid? ArchivedById, DateTimeOffset? ArchivedAt);

public sealed record CalendarEventTaskSummaryViewModel(
    Guid Id, string ShortId, string Title, Guid StatusId, bool MarksTaskComplete, string StatusCategory,
    int ProgressPercent, Guid ObjectiveId, string ObjectiveTitle);

public static class CalendarEventTaskSummaryViewModelMapper
{
    public static CalendarEventTaskSummaryViewModel ToViewModel(this CalendarEventTaskSummaryResponse r)
        => new(r.Id, r.ShortId, r.Title, r.StatusId, r.MarksTaskComplete, r.StatusCategory, r.ProgressPercent, r.ObjectiveId, r.ObjectiveTitle);
}

public sealed record CalendarEventActivityEntryViewModel(
    Guid Id, string Action, Guid PerformedById, string? PerformedByName, DateTimeOffset PerformedAt, string DetailsJson);

public static class CalendarEventActivityEntryViewModelMapper
{
    public static CalendarEventActivityEntryViewModel ToViewModel(this CalendarEventActivityEntryResponse r)
        => new(r.Id, r.Action, r.PerformedById, r.PerformedByName, r.PerformedAt, r.DetailsJson);
}

public static class CalendarViewModelMapper
{
    public static ProjectCalendarViewModel ToViewModel(this ProjectCalendarResponse response)
        => new(
            response.Modules.Select(m => new ProjectCalendarModuleViewModel(
                m.ObjectiveId, m.ProjectId, m.ParentObjectiveId, m.Title,
                m.StartDate, m.EndDate, m.IsActive, m.IsAchieved, m.CanEdit,
                m.Events.Select(e => new ProjectCalendarEventLinkViewModel(
                    e.EventId, e.EventName, e.EventColor, e.EventStartDate, e.EventEndDate,
                    e.Membership, e.TasksInEventCount, e.TaskTotalCount)).ToList(),
                m.ProgressPercent)).ToList(),
            response.Bands.Select(b => new ProjectCalendarEventBandViewModel(
                b.EventId, b.Name, b.Color, b.StartDate, b.EndDate, b.CanEdit)).ToList());

    public static CalendarEventViewModel ToViewModel(this CalendarEventResponse response)
        => new(response.Id, response.ProjectId, response.Name, response.Color, response.Status,
            response.StartDate, response.EndDate, response.Description, response.ObjectiveIds, response.TaskIds,
            response.CreatedAt, response.ArchivedById, response.ArchivedAt);

    public static CalendarEventDetailViewModel ToViewModel(this CalendarEventDetailResponse response)
        => new(response.Id, response.ProjectId, response.Name, response.Color, response.Status,
            response.StartDate, response.EndDate, response.Description, response.CreatedById, response.CreatedByName,
            response.ObjectiveIds, response.TaskIds, response.CreatedAt, response.ArchivedById, response.ArchivedAt);
}
