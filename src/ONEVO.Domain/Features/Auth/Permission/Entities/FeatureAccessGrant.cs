using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Auth.Entities;

public class FeatureAccessGrant : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string GranteeType { get; set; } = string.Empty; // "role" | "employee"
    public Guid GranteeId { get; set; }
    public string Module { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public Guid GrantedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
