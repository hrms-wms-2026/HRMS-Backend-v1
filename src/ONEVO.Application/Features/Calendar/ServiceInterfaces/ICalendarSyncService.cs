namespace ONEVO.Application.Features.Calendar.ServiceInterfaces;

/// <summary>Implemented in Task 10 (CalendarSyncService). Declared here so
/// TriggerCalendarSyncCommandHandler can depend on it without a forward reference to
/// Infrastructure — the background job (also Task 10) calls the same method per connection.</summary>
public interface ICalendarSyncService
{
    Task SyncConnectionAsync(Guid tenantId, Guid connectionId, CancellationToken ct);
}
