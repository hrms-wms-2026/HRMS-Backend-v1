using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;

/// <summary>
/// Normalized keyboard/mouse activity counts for a single capture interval.
/// Stores counts only — never keystroke content or mouse coordinates.
/// </summary>
public class ActivitySnapshot : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid AgentDeviceId { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public int KeyboardEventsCount { get; set; }
    public int MouseEventsCount { get; set; }
    public int ActiveSeconds { get; set; }
    public int IdleSeconds { get; set; }
    public decimal IntensityScore { get; set; }
    public string? ForegroundProcessName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
