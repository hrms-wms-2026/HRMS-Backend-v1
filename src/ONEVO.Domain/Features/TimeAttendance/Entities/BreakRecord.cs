using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.TimeAttendance.Entities;

public class BreakRecord : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateTimeOffset BreakStart { get; set; }
    public DateTimeOffset? BreakEnd { get; set; }

    /// <summary>lunch | prayer | smoke | personal | other</summary>
    public string BreakType { get; set; } = "personal";

    public bool AutoDetected { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>PostgreSQL xmin optimistic-concurrency token.</summary>
    public uint Version { get; set; }
}
