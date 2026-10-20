namespace ONEVO.Application.Features.Calendar.ServiceInterfaces;

/// <summary>Implemented in Task 10 (CalendarSyncService). Declared here so
/// TriggerCalendarSyncCommandHandler can depend on it without a forward reference to
/// Infrastructure — the background job (also Task 10) calls the same method per connection.</summary>
public interface ICalendarSyncService
{
    Task SyncConnectionAsync(Guid tenantId, Guid connectionId, CancellationToken ct);

    /// <summary>Pushes one event (with its attendees) to an Outlook connection right away, instead of waiting
    /// for the next scheduled run. Outlook then sends the attendees their invite / update itself.</summary>
    Task PushOutlookEventAsync(Guid tenantId, Guid connectionId, Guid calendarEventId, CancellationToken ct);

    /// <summary>Deletes an already-pushed event from Outlook; Outlook sends the attendees the cancellation.</summary>
    Task DeleteOutlookEventAsync(Guid tenantId, Guid connectionId, string externalEventId, CancellationToken ct);
}
