namespace ONEVO.Application.Features.Calendar.ServiceInterfaces;

public sealed record GraphEventDto(
    string Id,
    string? Etag,
    string Title,
    string? Description,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Timezone,
    string? Location,
    bool IsCancelled,
    bool IsPrivate);

public sealed record GraphCalendarPage(IReadOnlyList<GraphEventDto> Events, string? NextDeltaLink);

public interface IMicrosoftGraphCalendarClient
{
    Task<GraphCalendarPage> ListEventsAsync(string accessToken, string? deltaLink, DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken ct);
    Task<GraphEventDto> CreateEventAsync(string accessToken, GraphEventDto @event, CancellationToken ct);
    Task<GraphEventDto> UpdateEventAsync(string accessToken, string eventId, GraphEventDto @event, CancellationToken ct);
    Task DeleteEventAsync(string accessToken, string eventId, CancellationToken ct);
}
