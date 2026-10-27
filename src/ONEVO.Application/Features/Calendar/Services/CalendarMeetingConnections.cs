using System.Text.Json;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Services;

/// <summary>Which connection creates a Teams or Zoom meeting, and whether it may (spec §3.2.2): Teams through the organizer's
/// Microsoft (Outlook) connection with OnlineMeetings.ReadWrite, Zoom through their Zoom connection with meeting:write:meeting.
/// A connection made before the scope was added cannot create meetings until it is reconnected.</summary>
public static class CalendarMeetingConnections
{
    public static string SourceFor(string provider)
        => provider == CalendarEventMeetingProviders.Zoom ? CalendarExternalSources.Zoom : CalendarExternalSources.OutlookCalendar;

    public static string OAuthProviderFor(string provider) => provider == CalendarEventMeetingProviders.Zoom ? "zoom" : "microsoft";

    public static string RequiredScope(string provider)
        => provider == CalendarEventMeetingProviders.Zoom ? "meeting:write:meeting" : "OnlineMeetings.ReadWrite";

    public static bool IsKnownProvider(string? provider)
        => provider is CalendarEventMeetingProviders.MicrosoftTeams or CalendarEventMeetingProviders.Zoom;

    public static bool CanCreateMeetings(ExternalCalendarConnection? connection, string provider)
    {
        if (connection is not { Status: ExternalCalendarConnectionStatuses.Active })
            return false;
        try
        {
            var scopes = JsonSerializer.Deserialize<string[]>(connection.ScopesJson) ?? [];
            return scopes.Contains(RequiredScope(provider), StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
