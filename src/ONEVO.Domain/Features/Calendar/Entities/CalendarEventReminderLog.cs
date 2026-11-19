using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Calendar.Entities;

/// <summary>One start reminder sent to one participant for one occurrence (plan C1 P2-2); the row makes it "at most once".</summary>
public class CalendarEventReminderLog : BaseEntity
{
    public Guid EventId { get; set; }
    public DateTimeOffset OccurrenceStart { get; set; }
    public Guid EmployeeId { get; set; }
    public DateTimeOffset SentAt { get; set; }
}
