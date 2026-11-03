using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

/// <summary>An ordinary task raised from a meeting occurrence; a task links to at most one occurrence (spec 4.1, 5.4).</summary>
public class MeetingActionItem : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid OccurrenceTaskId { get; set; }
    public Guid TaskId { get; set; }
    public Guid CreatedByEmployeeId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
