using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities;

public static class CalendarEventStatuses
{
    public const string Active = "active";
    public const string Archived = "archived";
}

public class CalendarEvent : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Status { get; set; } = CalendarEventStatuses.Active;
    public Guid CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? ArchivedById { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}
