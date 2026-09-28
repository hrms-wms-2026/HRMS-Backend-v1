using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Calendar.Entities;

public class CalendarEventMeetingAttendance : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CalendarEventMeetingId { get; set; }
    public Guid? EmployeeId { get; set; }
    public string? ExternalParticipantName { get; set; }
    public string? ExternalParticipantEmail { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    public DateTimeOffset? LeftAt { get; set; }
    public int? DurationSeconds { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
