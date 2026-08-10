using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.AppUsage.Entities;

/// <summary>
/// Foreground-application usage sample. Window title is stored as a hash only —
/// raw title text is never sent by the tray agent or persisted here (§8.3).
/// </summary>
public class AppUsageSnapshot : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid AgentDeviceId { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public string? ProcessName { get; set; }
    public string? WindowTitleHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
