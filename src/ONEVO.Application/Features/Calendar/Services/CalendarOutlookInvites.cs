using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Services;

/// <summary>
/// When an organizer's Outlook connection pushes their events (with attendees), Outlook itself emails the invite,
/// update and cancellation. ONEVO's own email would land a second, differently-UID'd copy in every attendee's
/// calendar, so <see cref="CalendarNotificationSender"/> skips it and queues an immediate push instead. These two
/// predicates must match what <c>CalendarSyncService</c> actually pushes, or attendees would get no invite at all.
/// </summary>
public static class CalendarOutlookInvites
{
    public static bool PushesToOutlook(ExternalCalendarConnection connection)
        => connection.Provider == CalendarExternalSources.OutlookCalendar
           && connection.Status == ExternalCalendarConnectionStatuses.Active
           && connection.SyncDirection is CalendarSyncDirections.PushOnly or CalendarSyncDirections.TwoWay;

    // Same filter as ICalendarEventRepository.GetManualEventsUpdatedSinceForUserAsync: recurring events are never pushed.
    public static bool IsPushable(CalendarEvent calendarEvent)
        => calendarEvent.SourceType == CalendarEventSourceTypes.Manual && calendarEvent.Recurrence == CalendarRecurrences.None;
}
