using ONEVO.Application.Features.Calendar.DTOs.Responses;
using ONEVO.Application.Features.Calendar.Queries.CheckCalendarConflicts;

namespace ONEVO.Api.Contracts.Calendar;

public sealed record CreateCalendarEventRequest(
    string Title, string? Description, DateTimeOffset StartDate, DateTimeOffset EndDate,
    bool IsAllDay, string? Timezone, string? Location, string? MeetingLink, string? Color,
    string Recurrence, IReadOnlyList<Guid> ParticipantEmployeeIds, string? RecurrenceRule = null);

public sealed record UpdateCalendarEventRequest(
    string Title, string? Description, DateTimeOffset StartDate, DateTimeOffset EndDate,
    bool IsAllDay, string? Timezone, string? Location, string? MeetingLink, string? Color,
    string Recurrence);

public sealed record RespondToCalendarEventRequest(string ResponseStatus);

public sealed record MyEffectiveTimezoneViewModel(string Timezone);

public sealed record CheckCalendarConflictsRequest(IReadOnlyList<Guid> ParticipantEmployeeIds, DateTimeOffset StartDate, DateTimeOffset EndDate);
public sealed record CalendarConflictViewModel(Guid EmployeeId, string EmployeeName, Guid ConflictingEventId, string ConflictingEventTitle);
public sealed record CalendarConflictsViewModel(IReadOnlyList<CalendarConflictViewModel> Conflicts);

public sealed record EditRecurringOccurrenceRequest(
    DateTimeOffset OriginalStart, string Scope, string Title, string? Description,
    DateTimeOffset StartDate, DateTimeOffset EndDate, bool IsAllDay, string? Timezone,
    string? Location, string? MeetingLink, string? Color);

public sealed record CalendarEventParticipantSummaryViewModel(Guid EmployeeId, string EmployeeName, string ResponseStatus);

public sealed record CalendarEventViewModel(
    Guid Id, string Title, string? Description, DateTimeOffset StartDate, DateTimeOffset EndDate,
    string SourceType, string? Color, string Recurrence, bool IsAllDay, string? Timezone,
    string? EventStatus, bool IsPrivate, string? Location, string? MeetingLink,
    string? ExternalSource, Guid CreatedById,
    bool IsRecurringOccurrence = false, Guid? RecurrenceMasterId = null, DateTimeOffset? OriginalStart = null,
    IReadOnlyList<CalendarEventParticipantSummaryViewModel>? Participants = null);

public sealed record CalendarEventsViewModel(IReadOnlyList<CalendarEventViewModel> Events);

public static class CalendarEventViewModelMapper
{
    public static CalendarEventViewModel ToViewModel(this CalendarEventItem dto) => new(
        dto.Id, dto.Title, dto.Description, dto.StartDate, dto.EndDate, dto.SourceType, dto.Color,
        dto.Recurrence, dto.IsAllDay, dto.Timezone, dto.EventStatus, dto.IsPrivate, dto.Location,
        dto.MeetingLink, dto.ExternalSource, dto.CreatedById,
        dto.IsRecurringOccurrence, dto.RecurrenceMasterId, dto.OriginalStart,
        dto.Participants?.Select(p => new CalendarEventParticipantSummaryViewModel(p.EmployeeId, p.EmployeeName, p.ResponseStatus)).ToList());

    public static CalendarEventsViewModel ToViewModel(this CalendarEventsResponse dto) =>
        new(dto.Events.Select(e => e.ToViewModel()).ToList());
}

public static class CalendarConflictsViewModelMapper
{
    public static CalendarConflictsViewModel ToViewModel(this CalendarConflictsResponse dto) =>
        new(dto.Conflicts.Select(c => new CalendarConflictViewModel(c.EmployeeId, c.EmployeeName, c.ConflictingEventId, c.ConflictingEventTitle)).ToList());
}
