using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Auth.Entities;

/// <summary>Append-only audit trail. Never updated or deleted.</summary>
public class AuditLog : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public Guid? ResourceId { get; set; }
    public string? OldValuesJson { get; set; }
    public string? NewValuesJson { get; set; }
    public string? IpAddress { get; set; }
    public Guid? CorrelationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
