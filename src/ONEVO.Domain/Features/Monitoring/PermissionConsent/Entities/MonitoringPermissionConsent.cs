using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.PermissionConsent.Entities;

/// <summary>
/// One row per time an employee agreed to the tray's Privacy &amp; Transparency notice and
/// allowed its permissions. Append-only: the latest row is the employee's current consent,
/// earlier rows are the history. The tray skips both setup screens while a row covers what
/// the current monitoring policy needs.
/// </summary>
public class MonitoringPermissionConsent : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }

    /// <summary>The tray device the employee accepted on.</summary>
    public Guid DeviceRegistrationId { get; set; }

    public DateTimeOffset AcceptedAt { get; set; }

    public bool ScreenMonitoring { get; set; }
    public bool AppTracking { get; set; }
    public bool LocationAccess { get; set; }
    public bool CameraAccess { get; set; }
    public bool Notifications { get; set; }
    public bool KeyboardMouse { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
