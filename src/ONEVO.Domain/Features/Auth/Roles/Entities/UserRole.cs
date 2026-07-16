using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Auth.Entities;

public class UserRole : ITenantOwnedEntity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid AssignedBy { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }

    public Role Role { get; set; } = null!;
}
