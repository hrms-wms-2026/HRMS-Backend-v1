using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.ActivityMonitoring.Entities;

public class ActivitySnapshot : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public int KeyboardEventsCount { get; set; }
    public int MouseEventsCount { get; set; }
    public int ActiveSeconds { get; set; }
    public int IdleSeconds { get; set; }
    public decimal IntensityScore { get; set; }
    public string ForegroundProcessName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
