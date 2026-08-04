using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.CheckIn.Entities;

public class EmployeeCheckIn : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid DeviceRegistrationId { get; set; }

    // Location
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? LocationAccuracy { get; set; }   // metres
    public string? LocationAddress { get; set; }

    // Device
    public string? DeviceSerialNumber { get; set; }

    // Face scan link
    public Guid? FaceScanId { get; set; }
    public MonitoringFaceScan? FaceScan { get; set; }

    public DateTimeOffset CheckedInAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
