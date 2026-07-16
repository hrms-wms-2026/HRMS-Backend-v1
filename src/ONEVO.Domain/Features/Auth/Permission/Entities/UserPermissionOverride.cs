using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Auth.Entities;

public class UserPermissionOverride : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid PermissionId { get; set; }
    public string GrantType { get; set; } = string.Empty; // "grant" | "revoke"
    public string Reason { get; set; } = string.Empty;
    public Guid GrantedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Permission Permission { get; set; } = null!;
}
