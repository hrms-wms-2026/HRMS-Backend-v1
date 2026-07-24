using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.ActivityMonitoring.Entities;

public class MeetingSession : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateTimeOffset MeetingStart { get; set; }
    public DateTimeOffset MeetingEnd { get; set; }
    public string Platform { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public bool HadCameraOn { get; set; }
    public bool HadMicActivity { get; set; }
}
