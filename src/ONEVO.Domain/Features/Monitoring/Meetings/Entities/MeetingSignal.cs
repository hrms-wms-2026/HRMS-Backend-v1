using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.Meetings.Entities;

/// <summary>
/// Phase 1 probabilistic meeting-app-presence sample (process-name match only).
/// A row existing means a known meeting app was running at CapturedAt - not proof
/// the employee was actively in a meeting (architecture doc §7.4).
/// </summary>
public class MeetingSignal : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid AgentDeviceId { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public bool IsMeetingAppRunning { get; set; }
    public string? ProcessName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
