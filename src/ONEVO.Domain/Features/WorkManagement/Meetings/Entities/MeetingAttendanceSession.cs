using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

/// <summary>
/// One provider session (a Teams/Zoom instance) of a meeting link. An occurrence may have several sessions
/// (rejoins, restarts); <see cref="OccurrenceTaskId"/> is null until the session is matched to an occurrence
/// (spec 4.1, 5.8).
/// </summary>
public class MeetingAttendanceSession : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CalendarEventMeetingId { get; set; }
    public string ProviderSessionId { get; set; } = string.Empty;
    public DateTimeOffset SessionStart { get; set; }
    public DateTimeOffset? SessionEnd { get; set; }
    public Guid? OccurrenceTaskId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
