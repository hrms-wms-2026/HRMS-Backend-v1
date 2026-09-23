namespace ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities;

public class CalendarEventObjective
{
    public Guid Id { get; set; }
    public Guid CalendarEventId { get; set; }
    public Guid ObjectiveId { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}
