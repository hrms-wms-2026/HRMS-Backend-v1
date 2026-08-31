namespace ONEVO.Application.Features.Calendar.DTOs.Responses;

public sealed record CalendarEventParticipantSummary(Guid EmployeeId, string EmployeeName, string ResponseStatus);

public sealed record CalendarEventItem(
    Guid Id,
    string Title,
    string? Description,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    string SourceType,
    string? Color,
    string Recurrence,
    bool IsAllDay,
    string? Timezone,
    string? EventStatus,
    bool IsPrivate,
    string? Location,
    string? MeetingLink,
    string? ExternalSource,
    Guid CreatedById,
    bool IsRecurringOccurrence = false,
    Guid? RecurrenceMasterId = null,
    DateTimeOffset? OriginalStart = null,
    IReadOnlyList<CalendarEventParticipantSummary>? Participants = null);

public sealed record CalendarEventsResponse(IReadOnlyList<CalendarEventItem> Events);
