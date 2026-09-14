namespace ONEVO.Application.Features.Calendar.DTOs.Responses;

public sealed record CalendarConnectionItem(
    Guid Id,
    string Provider,
    string ExternalAccountEmail,
    string? ExternalCalendarName,
    string SyncDirection,
    string Status,
    DateTimeOffset? LastSyncedAt,
    string? LastError);

public sealed record CalendarConnectionsResponse(IReadOnlyList<CalendarConnectionItem> Connections);
