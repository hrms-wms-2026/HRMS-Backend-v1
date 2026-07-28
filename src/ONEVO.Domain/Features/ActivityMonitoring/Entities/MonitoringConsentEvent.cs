using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.ActivityMonitoring.Entities;

public class MonitoringConsentEvent : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid AgentDeviceId { get; set; }
    public Guid IncidentId { get; set; }
    public string Decision { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
}
