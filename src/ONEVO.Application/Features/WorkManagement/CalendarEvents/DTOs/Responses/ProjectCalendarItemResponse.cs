namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;

public static class ProjectCalendarEventMemberships
{
    public const string Whole = "whole";
    public const string Partial = "partial";
}

/// <summary>One event a module is drawn against on the project calendar (spec §5.4).
/// <c>Whole</c> = the module itself is a member; <c>Partial</c> = some of its tasks are.</summary>
public sealed record ProjectCalendarEventLink(
    Guid EventId,
    string EventName,
    string EventColor,
    DateOnly EventStartDate,
    DateOnly EventEndDate,
    string Membership,
    int TasksInEventCount,
    int TaskTotalCount);

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
    IReadOnlyList<ProjectCalendarEventLink> Events);

/// <summary>One translucent band drawn across its date span on the project calendar.</summary>
public sealed record ProjectCalendarEventBand(
    Guid EventId,
    string Name,
    string Color,
    DateOnly StartDate,
    DateOnly EndDate,
    bool CanEdit);

public sealed record ProjectCalendarResponse(
    IReadOnlyList<ProjectCalendarItemResponse> Modules,
    IReadOnlyList<ProjectCalendarEventBand> Bands);

public sealed record CalendarEventResponse(
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
