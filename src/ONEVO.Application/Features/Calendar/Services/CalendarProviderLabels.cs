using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Services;

/// <summary>How a calendar connection's provider is named to its owner in notifications.</summary>
public static class CalendarProviderLabels
{
    public static string For(string provider) => provider switch
    {
        CalendarExternalSources.GoogleCalendar => "Google Calendar",
        CalendarExternalSources.OutlookCalendar => "Outlook Calendar",
        CalendarExternalSources.Zoom => "Zoom",
        _ => "calendar",
    };
}
