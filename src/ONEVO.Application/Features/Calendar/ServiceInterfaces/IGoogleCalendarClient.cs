namespace ONEVO.Application.Features.Calendar.ServiceInterfaces;

public sealed record GoogleCalendarEventDto(
    string Id,
    string? Etag,
    string Title,
    string? Description,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Timezone,
    string? Location,
    bool IsCancelled);

public sealed record GoogleCalendarPage(IReadOnlyList<GoogleCalendarEventDto> Events, string? NextSyncToken);

public interface IGoogleCalendarClient
{
    Task<GoogleCalendarPage> ListEventsAsync(string accessToken, string calendarId, string? syncToken, DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken ct);
    Task<GoogleCalendarEventDto> InsertEventAsync(string accessToken, string calendarId, GoogleCalendarEventDto @event, CancellationToken ct);
    Task<GoogleCalendarEventDto> PatchEventAsync(string accessToken, string calendarId, string eventId, GoogleCalendarEventDto @event, CancellationToken ct);
    Task DeleteEventAsync(string accessToken, string calendarId, string eventId, CancellationToken ct);
}
