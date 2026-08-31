using ONEVO.Application.Features.Calendar.DTOs.Responses;

namespace ONEVO.Api.Contracts.Calendar;

public sealed record CreateCalendarEventRequest(
    string Title, string? Description, DateTimeOffset StartDate, DateTimeOffset EndDate,
    bool IsAllDay, string? Timezone, string? Location, string? MeetingLink, string? Color,
    string Recurrence, IReadOnlyList<Guid> ParticipantEmployeeIds, string? RecurrenceRule = null);

public sealed record UpdateCalendarEventRequest(
    string Title, string? Description, DateTimeOffset StartDate, DateTimeOffset EndDate,
    bool IsAllDay, string? Timezone, string? Location, string? MeetingLink, string? Color,
    string Recurrence);

public sealed record EditRecurringOccurrenceRequest(
    DateTimeOffset OriginalStart, string Scope, string Title, string? Description,
    DateTimeOffset StartDate, DateTimeOffset EndDate, bool IsAllDay, string? Timezone,
    string? Location, string? MeetingLink, string? Color);

public sealed record CalendarEventViewModel(
    Guid Id, string Title, string? Description, DateTimeOffset StartDate, DateTimeOffset EndDate,
    string SourceType, string? Color, string Recurrence, bool IsAllDay, string? Timezone,
    string? EventStatus, bool IsPrivate, string? Location, string? MeetingLink,
    string? ExternalSource, Guid CreatedById,
    bool IsRecurringOccurrence = false, Guid? RecurrenceMasterId = null, DateTimeOffset? OriginalStart = null);

public sealed record CalendarEventsViewModel(IReadOnlyList<CalendarEventViewModel> Events);

public static class CalendarEventViewModelMapper
{
    public static CalendarEventViewModel ToViewModel(this CalendarEventItem dto) => new(
        dto.Id, dto.Title, dto.Description, dto.StartDate, dto.EndDate, dto.SourceType, dto.Color,
        dto.Recurrence, dto.IsAllDay, dto.Timezone, dto.EventStatus, dto.IsPrivate, dto.Location,
        dto.MeetingLink, dto.ExternalSource, dto.CreatedById,
        dto.IsRecurringOccurrence, dto.RecurrenceMasterId, dto.OriginalStart);

    public static CalendarEventsViewModel ToViewModel(this CalendarEventsResponse dto) =>
        new(dto.Events.Select(e => e.ToViewModel()).ToList());
}
