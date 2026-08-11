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

    /// <summary>When this role was generated from a position's access template (rather than
    /// assigned directly), the position and template it came from. Both null for a
    /// directly-assigned role.</summary>
    public Guid? SourcePositionId { get; set; }
    public Guid? SourcePositionAccessTemplateId { get; set; }

    public Role Role { get; set; } = null!;
}
