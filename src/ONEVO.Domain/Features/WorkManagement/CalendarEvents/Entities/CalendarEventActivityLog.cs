using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities;

public static class CalendarEventActivityActions
{
    public const string Created = "created";
    public const string Updated = "updated";
    public const string Closed = "closed";
}

/// <summary>Audit row for a milestone's own Activity tab - same shape as Tasks.Entities.TaskEditLog,
/// kept separate because it is keyed on CalendarEventId, not TaskId.</summary>
public class CalendarEventActivityLog : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CalendarEventId { get; set; }
    public string Action { get; set; } = CalendarEventActivityActions.Created;
    public Guid PerformedById { get; set; }
    public DateTimeOffset PerformedAt { get; set; } = DateTimeOffset.UtcNow;
    public string DetailsJson { get; set; } = "{}";
}
