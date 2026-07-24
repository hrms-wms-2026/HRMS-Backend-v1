using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.ActivityMonitoring.Entities;

public class DeviceTracking : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly Date { get; set; }
    public int LaptopActiveMinutes { get; set; }
    public int EstimatedMobileMinutes { get; set; }
    public decimal LaptopPercentage { get; set; }

    /// <summary>agent | manual</summary>
    public string DetectionMethod { get; set; } = "agent";
}
