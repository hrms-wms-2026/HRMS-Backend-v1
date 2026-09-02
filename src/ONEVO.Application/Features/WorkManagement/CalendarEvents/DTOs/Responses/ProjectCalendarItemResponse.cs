namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;

public sealed record ProjectCalendarItemResponse(
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

public sealed record CalendarEventResponse(
    Guid Id,
    Guid ProjectId,
    string Name,
    string Color,
    string Status,
    IReadOnlyList<Guid> ObjectiveIds,
    DateTimeOffset CreatedAt,
    Guid? ArchivedById,
    DateTimeOffset? ArchivedAt);
