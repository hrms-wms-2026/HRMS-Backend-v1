using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.TimeAttendance.Entities;

/// <summary>
/// A remote/WFH employee's registered reference point, used to validate later check-ins against
/// (mirrors what LegalEntity's office point does for onsite employees). One row per employee -
/// auto-registered from their first real clock-in while working remotely, never from a separate
/// "confirm your location" screen. Only ever replaced through an approved LocationChangeRequest
/// the employee then opts to apply on a later clock-in.
/// </summary>
public sealed class EmployeeWorkLocation : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? AccuracyMeters { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
