using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Calendar.Entities;

public static class CalendarEventSourceTypes
{
    public const string Manual = "manual";
    public const string ExternalSync = "external_sync";
    // "holiday" / "schedule_overlay" / "time_off_request" are reserved for later specs -
    // this pass never writes them, but the column must accept them without a future migration.
}

public static class CalendarExternalSources
{
    public const string GoogleCalendar = "google_calendar";
    public const string OutlookCalendar = "outlook_calendar";
}

public static class CalendarRecurrences
{
    public const string None = "none";
    public const string Daily = "daily";
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";
}

public static class CalendarEventStatuses
{
    public const string Confirmed = "confirmed";
    public const string Tentative = "tentative";
    public const string Cancelled = "cancelled";
}

public class CalendarEvent : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset StartDate { get; set; }
    public DateTimeOffset EndDate { get; set; }
    public string SourceType { get; set; } = CalendarEventSourceTypes.Manual;
    public Guid? SourceId { get; set; }
    public string? Color { get; set; }
    public string Recurrence { get; set; } = CalendarRecurrences.None;
    public string? ExternalId { get; set; }
    public string? ExternalSource { get; set; }
    public bool IsAllDay { get; set; }
    public string? Timezone { get; set; }
    public string? EventStatus { get; set; }
    public bool IsPrivate { get; set; }
    public string? OrganizerName { get; set; }
    public string? OrganizerEmail { get; set; }
    public string? Location { get; set; }
    public string? MeetingLink { get; set; }
    public string? ExternalAttendeesJson { get; set; }
    public string? RecurrenceRule { get; set; }
    public DateTimeOffset? ExternalUpdatedAt { get; set; }
    public Guid? RecurrenceParentId { get; set; }
    public DateTimeOffset? RecurrenceOriginalStart { get; set; }
    public bool IsRecurrenceCancelled { get; set; }
}
