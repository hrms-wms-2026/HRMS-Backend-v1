namespace ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities;

/// <summary>Links an Event to a single Task (spec §4). Sits alongside CalendarEventObjective
/// (whole-module, live). No TenantId - child of the tenant-owned calendar_events row.</summary>
public class CalendarEventTask
{
    public Guid Id { get; set; }
    public Guid CalendarEventId { get; set; }
    public Guid TaskId { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}
