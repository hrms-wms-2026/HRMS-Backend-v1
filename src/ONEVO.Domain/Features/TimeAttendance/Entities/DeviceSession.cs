using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.TimeAttendance.Entities;

public class DeviceSession : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid DeviceId { get; set; }
    public DateTimeOffset SessionStart { get; set; }
    public DateTimeOffset? SessionEnd { get; set; }
    public int ActiveMinutes { get; set; }
    public int IdleMinutes { get; set; }
    public decimal ActivePercentage { get; set; }

    /// <summary>PostgreSQL xmin optimistic-concurrency token.</summary>
    public uint Version { get; set; }
}
