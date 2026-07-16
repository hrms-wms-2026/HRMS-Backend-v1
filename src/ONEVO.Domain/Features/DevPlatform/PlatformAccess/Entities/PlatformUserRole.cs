namespace ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

/// <summary>
/// Assigns a platform role to a platform user (canonical `platform_user_roles`).
/// Composite PK (user_id, role_id). AssignedById is null only for the bootstrap
/// Super Admin assignment, mirroring the nullable-for-seed rule on platform_users.created_by_id.
/// </summary>
public class PlatformUserRole
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public Guid? AssignedById { get; set; }
    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;
}
