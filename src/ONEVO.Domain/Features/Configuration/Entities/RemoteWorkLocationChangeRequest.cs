using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Configuration.Entities;

public class RemoteWorkLocationChangeRequest : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? CurrentProfileId { get; set; }
    public string Reason { get; set; } = string.Empty;

    /// <summary>pending | approved | rejected | captured | expired</summary>
    public string Status { get; set; } = "pending";

    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? ReviewedById { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewComment { get; set; }
    public Guid? NewProfileId { get; set; }

    /// <summary>PostgreSQL xmin optimistic-concurrency token.</summary>
    public uint Version { get; set; }
}
