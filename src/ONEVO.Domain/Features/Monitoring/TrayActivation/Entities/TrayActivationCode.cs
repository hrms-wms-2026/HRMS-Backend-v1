using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;

public class TrayActivationCode : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid? LegalEntityId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsValid => UsedAt is null && DateTimeOffset.UtcNow < ExpiresAt;
}
