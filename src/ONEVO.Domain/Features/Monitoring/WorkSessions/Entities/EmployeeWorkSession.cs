using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.WorkSessions.Entities;

public class EmployeeWorkSession : ITenantOwnedEntity
{
    // Client-generated (== the device's PresenceSession.CurrentSessionId) — the
    // upsert key. A retried delivery after a dropped response is a no-op, never
    // a duplicate row.
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid DeviceRegistrationId { get; set; }

    public DateTimeOffset ClockInAt { get; set; }
    public DateTimeOffset ClockOutAt { get; set; }
    public int AccumulatedBreakSeconds { get; set; }
    public int AccumulatedWorkSeconds { get; set; }
    public int BreakSessionCount { get; set; }
    public string? ScheduleDisplay { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
